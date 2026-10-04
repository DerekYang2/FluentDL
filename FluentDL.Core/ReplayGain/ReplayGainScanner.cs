#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using FFMpegCore;

namespace FluentDL.Core.ReplayGain;

// Decodes tracks with ffmpeg, measures them, and writes ReplayGain tags with TagLib.
// ffmpeg's location comes from FFMpegCore's GlobalFFOptions, which the app sets at startup.
public static class ReplayGainScanner
{
    // Shared by every scan, so downloads and the Local Explorer dialog together measure at most 4 tracks at once.
    // Each track keeps about two cores busy, ffmpeg decoding and this process measuring. More than 4 at once
    // saved under 10% in testing and leaves less CPU for the rest of the app.
    private static readonly SemaphoreSlim MeasureLimit = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

    private static readonly ReplayGainTagValues NoTags = new(null, null, null, null);

    // What a scan knows about one input file.
    private sealed class Entry(ReplayGainTrack track)
    {
        // The track, with album details missing from the input filled in from the file's tags.
        public ReplayGainTrack Track = track;
        public FileTags? Tags;
        public Album? Album;
        public bool Skipped;
        // Kept only until the track's album is finished, since album gain needs the meters of all its tracks.
        public LoudnessMeasurement? Measurement;
        public ReplayGainResult? Result;
        public ReplayGainFailure? Failure;
    }

    private sealed class Album(List<Entry> entries)
    {
        public readonly List<Entry> Entries = entries;
        // Tracks still being measured. The track that brings this to 0 calculates the album.
        public int Pending = entries.Count;
    }

    private sealed record FileTags(string? Album, string? AlbumArtist, ReplayGainTagValues Existing);

    public static async Task<ReplayGainScan> ScanAsync(
        IReadOnlyList<ReplayGainTrack> tracks,
        ReplayGainOptions options,
        IProgress<ReplayGainProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // A file listed twice is measured once.
        var entries = tracks.DistinctBy(track => track.Path, StringComparer.OrdinalIgnoreCase).Select(track => new Entry(track)).ToArray();
        try
        {
            if (ReadsTagsFirst(options))
            {
                await Task.Run(() => Parallel.ForEach(entries,
                        new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, LoadTags),
                    cancellationToken).ConfigureAwait(false);
                PlanAlbums(entries, options);
            }

            var toMeasure = entries.Where(entry => !entry.Skipped).ToList();
            var completed = 0;
            progress?.Report(new ReplayGainProgress(0, toMeasure.Count, null, null));
            await Task.WhenAll(toMeasure.Select(async entry =>
            {
                await MeasureLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await MeasureAsync(entry, options, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    MeasureLimit.Release();
                }

                // Progress carries the track's own values. Album values arrive with the finished scan.
                var trackResult = entry.Result;
                if (entry.Album is { } album && Interlocked.Decrement(ref album.Pending) == 0) FinishAlbum(album, options);
                progress?.Report(new ReplayGainProgress(Interlocked.Increment(ref completed), toMeasure.Count, trackResult, entry.Failure));
            })).ConfigureAwait(false);

            return new ReplayGainScan(
                entries.Select(entry => entry.Result).OfType<ReplayGainResult>().ToList(),
                entries.Select(entry => entry.Failure).OfType<ReplayGainFailure>().ToList(),
                entries.Where(entry => entry.Skipped).Select(entry => entry.Track.Path).ToList());
        }
        finally
        {
            foreach (var entry in entries) entry.Measurement?.Dispose();
        }
    }

    // Album gain and skipping both need every file's album and tags before anything is measured.
    private static bool ReadsTagsFirst(ReplayGainOptions options) => options.AlbumGain || options.SkipTaggedTracks;

    private static void LoadTags(Entry entry)
    {
        entry.Tags = ReadFileTags(entry.Track.Path);
        if (entry.Tags is { } tags)
            entry.Track = entry.Track with { Album = entry.Track.Album ?? tags.Album, AlbumArtist = entry.Track.AlbumArtist ?? tags.AlbumArtist };
    }

    // Groups tracks into albums for album gain, and marks the tracks that skipping leaves out. Without album gain,
    // each track is its own group, so it's skipped on its own tags. A file whose tags couldn't be read counts as
    // untagged, so its group is measured.
    private static void PlanAlbums(Entry[] entries, ReplayGainOptions options)
    {
        var groups = options.AlbumGain
            ? ReplayGainMath.GroupByAlbum(entries, entry => entry.Track, options.SingleAlbum)
            : entries.Select(entry => new List<Entry> { entry }).ToList();
        foreach (var group in groups)
        {
            if (options.SkipTaggedTracks && group.All(entry => HasRequestedTags(entry.Tags?.Existing, options)))
            {
                foreach (var entry in group) entry.Skipped = true;
            }
            else if (options.AlbumGain)
            {
                var album = new Album(group);
                foreach (var entry in group) entry.Album = album;
            }
        }
    }

    private static bool HasRequestedTags(ReplayGainTagValues? tags, ReplayGainOptions options) =>
        tags is not null
        && (!options.TrackGain || tags is { TrackGain: not null, TrackPeak: not null })
        && (!options.AlbumGain || tags is { AlbumGain: not null, AlbumPeak: not null });

    private static async Task MeasureAsync(Entry entry, ReplayGainOptions options, CancellationToken cancellationToken)
    {
        var path = entry.Track.Path;
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}", path);
            if (!ReadsTagsFirst(options)) await Task.Run(() => LoadTags(entry), cancellationToken).ConfigureAwait(false);
            entry.Measurement = await DecodeAsync(path, cancellationToken).ConfigureAwait(false);
            entry.Result = ReplayGainCalculator.Calculate([[(path, entry.Measurement)]], options with { AlbumGain = false })[0]
                with { Existing = entry.Tags?.Existing };
            if (entry.Album is null) ReleaseMeasurement(entry);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            entry.Failure = new ReplayGainFailure(path, FirstLine(e.Message));
        }
    }

    // Album gain needs every track on the album, so an album with a track that couldn't be measured keeps its
    // track values only. Either way, the album's meters are released.
    private static void FinishAlbum(Album album, ReplayGainOptions options)
    {
        try
        {
            if (album.Entries.Any(entry => entry.Failure is not null)) return;
            var results = ReplayGainCalculator.Calculate([album.Entries.Select(entry => (entry.Track.Path, entry.Measurement!)).ToList()], options);
            for (var index = 0; index < results.Count; index++)
                album.Entries[index].Result = results[index] with { Existing = album.Entries[index].Tags?.Existing };
        }
        finally
        {
            foreach (var entry in album.Entries) ReleaseMeasurement(entry);
        }
    }

    private static void ReleaseMeasurement(Entry entry)
    {
        entry.Measurement?.Dispose();
        entry.Measurement = null;
    }

    // Reading ffmpeg's output blocks, so each track gets its own thread instead of a thread pool thread.
    private static Task<LoudnessMeasurement> DecodeAsync(string path, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(() => Decode(path, cancellationToken), cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // Starts ffmpeg directly rather than through FFMpegCore, which keeps about five thread pool threads waiting
    // for each process; with several tracks at once that starved the pool and slowed the whole app. ffmpeg writes
    // 32-bit float WAV in the stream's own format, and the WAV header says what that format is.
    private static LoudnessMeasurement Decode(string path, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(GlobalFFOptions.GetFFMpegBinaryPath())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-nostdin", "-loglevel", "error", "-i", path, "-vn", "-sn", "-dn",
            "-map_metadata", "-1", "-fflags", "+bitexact", "-c:a", "pcm_f32le", "-f", "wav", "pipe:1",
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg didn't start.");
        // ffmpeg stops when its error output is full, so a second thread keeps reading it.
        var errors = Task.Factory.StartNew(() => process.StandardError.ReadToEnd(),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        LoudnessMeasurement? measurement = null;
        try
        {
            using (cancellationToken.Register(() => Stop(process)))
            {
                var audio = process.StandardOutput.BaseStream;
                if (WavFormat.Read(audio) is { } format)
                {
                    measurement = new LoudnessMeasurement(format.Channels, format.SampleRate, format.ChannelMask);
                    measurement.Read(audio);
                }
                process.WaitForExit();
            }

            var errorText = errors.Result;
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                var lastLine = errorText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                throw new InvalidOperationException(lastLine ?? $"ffmpeg exited with code {process.ExitCode}.");
            }
            return measurement ?? throw new InvalidDataException("ffmpeg didn't write any audio.");
        }
        catch
        {
            Stop(process);
            measurement?.Dispose();
            throw;
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // It already exited.
        }
    }

    // Writes the gains that the options ask for. Other ReplayGain tags in the file stay as they are.
    // A file that already has exactly these values is left untouched.
    public static void WriteTags(ReplayGainResult result, ReplayGainOptions options)
    {
        if (HasTags(result, options)) return;
        using var file = TagLib.File.Create(result.Path);
        var tag = PreferredTag(file, create: true) ?? file.Tag;
        var writesAlbum = options.AlbumGain && result.AlbumGain is not null;
        if (options.TrackGain)
        {
            tag.ReplayGainTrackGain = result.TrackGain;
            tag.ReplayGainTrackPeak = result.TrackPeak;
        }
        if (writesAlbum)
        {
            tag.ReplayGainAlbumGain = result.AlbumGain!.Value;
            tag.ReplayGainAlbumPeak = result.AlbumPeak!.Value;
        }

        // Some formats' tags ignore ReplayGain values. Saving those would change nothing yet look like success.
        if ((options.TrackGain && double.IsNaN(tag.ReplayGainTrackGain)) || (writesAlbum && double.IsNaN(tag.ReplayGainAlbumGain)))
            throw new NotSupportedException($"{Path.GetExtension(result.Path)} files can't store ReplayGain tags.");

        // TagLib adds tag types the file didn't have, such as ID3v1 on MP3. Only the ReplayGain tag should be new.
        file.RemoveTags(file.TagTypes & ~file.TagTypesOnDisk & ~tag.TagTypes);
        file.Save();
    }

    // True when the file's tags before the scan already hold the values WriteTags would write. Values are
    // compared as tag text, which keeps two decimals for gains and six for peaks.
    public static bool HasTags(ReplayGainResult result, ReplayGainOptions options)
    {
        if (result.Existing is not { } tags) return false;
        if (options.TrackGain && !(SameText(tags.TrackGain, result.TrackGain, GainFormat) && SameText(tags.TrackPeak, result.TrackPeak, PeakFormat)))
            return false;
        if (options.AlbumGain && result.AlbumGain is { } albumGain
            && !(SameText(tags.AlbumGain, albumGain, GainFormat) && SameText(tags.AlbumPeak, result.AlbumPeak!.Value, PeakFormat)))
            return false;
        return true;
    }

    // The formats TagLib uses when it writes ReplayGain values.
    private const string GainFormat = "0.00";
    private const string PeakFormat = "0.000000";

    private static bool SameText(double? stored, double value, string format) =>
        stored is { } storedValue
        && storedValue.ToString(format, CultureInfo.InvariantCulture) == value.ToString(format, CultureInfo.InvariantCulture);

    public static List<ReplayGainFailure> WriteTags(IEnumerable<ReplayGainResult> results, ReplayGainOptions options)
    {
        var failures = new List<ReplayGainFailure>();
        foreach (var result in results)
        {
            try
            {
                WriteTags(result, options);
            }
            catch (Exception e)
            {
                failures.Add(new ReplayGainFailure(result.Path, FirstLine(e.Message)));
            }
        }
        return failures;
    }

    // Reads the ReplayGain values in the tag that WriteTags writes to, in any letter case.
    public static ReplayGainTagValues ReadTags(string path)
    {
        using var file = TagLib.File.Create(path);
        return TagValues(file);
    }

    private static ReplayGainTagValues TagValues(TagLib.File file) =>
        PreferredTag(file, create: false) is { } tag
            ? new ReplayGainTagValues(
                NullIfNaN(tag.ReplayGainTrackGain), NullIfNaN(tag.ReplayGainTrackPeak),
                NullIfNaN(tag.ReplayGainAlbumGain), NullIfNaN(tag.ReplayGainAlbumPeak))
            : NoTags;

    private static double? NullIfNaN(double value) => double.IsNaN(value) ? null : value;

    // Measures one file and writes its track gain, as used after a download.
    public static async Task<ReplayGainResult> ApplyTrackGainAsync(
        string path, double offset, bool preventClipping, CancellationToken cancellationToken = default)
    {
        var options = new ReplayGainOptions(TrackGain: true, AlbumGain: false, Offset: offset, PreventClipping: preventClipping);
        var scan = await ScanAsync([new ReplayGainTrack(path)], options, null, cancellationToken).ConfigureAwait(false);
        if (scan.Failures.Count > 0) throw new InvalidOperationException(scan.Failures[0].Message);
        var result = scan.Results[0];
        await Task.Run(() => WriteTags(result, options), cancellationToken).ConfigureAwait(false);
        return result;
    }

    // Each format has one standard place for ReplayGain tags: ID3v2 for MP3, WAV and AIFF, Vorbis comments for
    // FLAC, Ogg Vorbis and Opus, and iTunes metadata for M4A. Other formats use whatever tags the file has.
    private static TagLib.Tag? PreferredTag(TagLib.File file, bool create) => file switch
    {
        TagLib.Mpeg.AudioFile or TagLib.Riff.File or TagLib.Aiff.File => file.GetTag(TagLib.TagTypes.Id3v2, create),
        TagLib.Flac.File or TagLib.Ogg.File => file.GetTag(TagLib.TagTypes.Xiph, create),
        TagLib.Mpeg4.File => file.GetTag(TagLib.TagTypes.Apple, create),
        _ => file.Tag,
    };

    private static FileTags? ReadFileTags(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            return new FileTags(NullIfBlank(file.Tag.Album), NullIfBlank(file.Tag.JoinedAlbumArtists), TagValues(file));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // ffmpeg may still decode a file TagLib can't read, so it's measured without album details or existing
            // tags. A real problem, such as a missing file, is reported when the track is measured.
            return null;
        }
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static string FirstLine(string message) => message.Split('\n', 2)[0].Trim();
}
