using System.Diagnostics;
using System.Runtime.InteropServices;
using FFMpegCore;
using FluentDL.Core.ReplayGain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

// End-to-end tests that decode real files with the ffmpeg.exe bundled in FluentDL\Assets\ffmpeg for this machine's
// architecture.
[TestClass]
public class ReplayGainScannerTests
{
    private static string? ffmpeg;
    private static string folder = "";

    [ClassInitialize]
    public static void FindFfmpeg(TestContext context)
    {
        var runtime = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "FluentDL", "Assets", "ffmpeg", runtime, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                ffmpeg = candidate;
                break;
            }
        }
        if (ffmpeg is not null) GlobalFFOptions.Configure(options => options.BinaryFolder = Path.GetDirectoryName(ffmpeg)!);
        folder = Path.Combine(Path.GetTempPath(), "fluentdl-replaygain-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    [ClassCleanup]
    public static void RemoveFiles()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    // Writes a 10 second, 1 kHz stereo sine at the given level in dBFS, with ffmpeg's default tags.
    private static string MakeSine(string name, double dbfs, string codecArguments)
    {
        var expression = Sine(dbfs);
        return RunFfmpeg(name, $"-f lavfi -i \"aevalsrc={expression}|{expression}:s=48000:d=10\" {codecArguments}");
    }

    // Writes a 5.1 FLAC file with a -20 dBFS sine in one channel and silence in the others.
    private static string MakeSurround(string name, int activeChannel)
    {
        var expressions = Enumerable.Range(0, 6).Select(channel => channel == activeChannel ? Sine(-20) : "0");
        return RunFfmpeg(name, $"-f lavfi -i \"aevalsrc={string.Join('|', expressions)}:c=5.1:s=48000:d=10\" -c:a flac");
    }

    private static string Sine(double dbfs) =>
        $"{Math.Pow(10, dbfs / 20).ToString("R", System.Globalization.CultureInfo.InvariantCulture)}*sin(2*PI*1000*t)";

    private static string RunFfmpeg(string name, string arguments)
    {
        if (ffmpeg is null) Assert.Inconclusive("The bundled ffmpeg.exe was not found.");
        var path = Path.Combine(folder, name);
        var start = new ProcessStartInfo(ffmpeg!)
        {
            Arguments = $"-hide_banner -loglevel error -y {arguments} \"{path}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, errors);
        return path;
    }

    [DataTestMethod]
    [DataRow("sine.flac", "-c:a flac", 0.1)]
    [DataRow("sine.mp3", "-c:a libmp3lame -b:a 320k", 0.2)]
    [DataRow("sine.m4a", "-c:a aac -b:a 256k", 0.2)]
    [DataRow("sine.opus", "-c:a libopus -b:a 192k", 0.2)]
    public async Task ScanAndWrite_StoresTheTrackGainWhereTaglibReadsIt(string name, string codec, double tolerance)
    {
        // Arrange: a -23 dBFS sine measures -23 LUFS, so it needs +5 dB.
        var path = MakeSine(name, -23, codec);

        // Act
        var result = await ReplayGainScanner.ApplyTrackGainAsync(path, offset: 0, preventClipping: false);

        // Assert
        Assert.AreEqual(-23.0, result.Loudness, tolerance);
        Assert.AreEqual(5.0, result.TrackGain, tolerance);
        using var file = TagLib.File.Create(path);
        Assert.AreEqual(result.TrackGain, file.Tag.ReplayGainTrackGain, 0.005);
        Assert.AreEqual(result.TrackPeak, file.Tag.ReplayGainTrackPeak, 1e-5);
        Assert.IsTrue(double.IsNaN(file.Tag.ReplayGainAlbumGain), "Track-only writes leave album gain unset.");
    }

    [TestMethod]
    public async Task ScanAndWrite_UsesTheStandardTagNames()
    {
        // Arrange
        var flac = MakeSine("names.flac", -20, "-c:a flac");
        var mp3 = MakeSine("names.mp3", -20, "-c:a libmp3lame -b:a 320k");
        var options = new ReplayGainOptions();

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(flac, "Album", "Artist"), new ReplayGainTrack(mp3, "Album", "Artist")], options);
        var failures = ReplayGainScanner.WriteTags(scan.Results, options);

        // Assert
        Assert.AreEqual(0, scan.Failures.Count);
        Assert.AreEqual(0, failures.Count);
        using (var file = TagLib.File.Create(flac))
        {
            var xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph);
            StringAssert.EndsWith(xiph.GetFirstField("REPLAYGAIN_TRACK_GAIN"), " dB");
            StringAssert.EndsWith(xiph.GetFirstField("REPLAYGAIN_ALBUM_GAIN"), " dB");
            Assert.IsNotNull(xiph.GetFirstField("REPLAYGAIN_TRACK_PEAK"));
            Assert.IsNotNull(xiph.GetFirstField("REPLAYGAIN_ALBUM_PEAK"));
        }
        using (var file = TagLib.File.Create(mp3))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
            var descriptions = id3.GetFrames<TagLib.Id3v2.UserTextInformationFrame>().Select(frame => frame.Description).ToList();
            CollectionAssert.IsSubsetOf(
                new[] { "REPLAYGAIN_TRACK_GAIN", "REPLAYGAIN_TRACK_PEAK", "REPLAYGAIN_ALBUM_GAIN", "REPLAYGAIN_ALBUM_PEAK" },
                descriptions.Select(description => description.ToUpperInvariant()).ToList());
        }
    }

    [TestMethod]
    public async Task Scan_GroupsAlbumsByTheFilesOwnTags()
    {
        // Arrange: two tracks in one folder, tagged with different albums.
        var quiet = MakeSine("album-one.flac", -20, "-c:a flac -metadata album=One -metadata album_artist=Artist");
        var loud = MakeSine("album-two.flac", -10, "-c:a flac -metadata album=Two -metadata album_artist=Artist");

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(quiet), new ReplayGainTrack(loud)], new ReplayGainOptions());
        var together = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(quiet), new ReplayGainTrack(loud)], new ReplayGainOptions(SingleAlbum: true));

        // Assert: as separate albums, each album gain equals that track's gain; as one album, they share a value.
        Assert.AreEqual(scan.Results[0].TrackGain, scan.Results[0].AlbumGain!.Value, 1e-9);
        Assert.AreEqual(scan.Results[1].TrackGain, scan.Results[1].AlbumGain!.Value, 1e-9);
        Assert.AreEqual(together.Results[0].AlbumGain, together.Results[1].AlbumGain);
        Assert.AreNotEqual(together.Results[0].TrackGain, together.Results[0].AlbumGain!.Value, 0.5);
    }

    [TestMethod]
    public async Task Scan_MeasuresAFileListedTwiceOnce()
    {
        // Arrange: the same file under two spellings of its path.
        var path = MakeSine("twice.flac", -20, "-c:a flac");

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(path), new ReplayGainTrack(path.ToUpperInvariant())], new ReplayGainOptions());

        // Assert
        Assert.AreEqual(1, scan.Results.Count);
        Assert.AreEqual(0, scan.Failures.Count);
    }

    [TestMethod]
    public async Task Scan_ReportsUnreadableFilesWithoutStoppingTheOthers()
    {
        // Arrange
        var good = MakeSine("good.flac", -20, "-c:a flac");
        var broken = Path.Combine(folder, "broken.flac");
        await File.WriteAllTextAsync(broken, "not audio");

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(good), new ReplayGainTrack(broken)], new ReplayGainOptions());

        // Assert
        Assert.AreEqual(1, scan.Results.Count);
        Assert.AreEqual(good, scan.Results[0].Path);
        Assert.AreEqual(1, scan.Failures.Count);
        Assert.AreEqual(broken, scan.Failures[0].Path);
    }

    [TestMethod]
    public async Task Scan_ReportsEachTrackAsItFinishes()
    {
        // Arrange: the readable track has its own album, so the unreadable one doesn't hold back its album gain.
        var quiet = MakeSine("progress-quiet.flac", -20, "-c:a flac -metadata album=Progress -metadata album_artist=Artist");
        var broken = Path.Combine(folder, "progress-broken.flac");
        await File.WriteAllTextAsync(broken, "not audio");
        var progress = new CollectingProgress();

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(quiet), new ReplayGainTrack(broken)], new ReplayGainOptions(), progress);

        // Assert: a first report with the total, then one per track with track values only. Album gain comes with the finished scan.
        Assert.AreEqual(3, progress.Reports.Count);
        Assert.AreEqual(new ReplayGainProgress(0, 2, null, null), progress.Reports[0]);
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, progress.Reports.Skip(1).Select(report => report.Completed).ToList());
        var measured = progress.Reports.Single(report => report.Track is not null).Track!;
        Assert.AreEqual(scan.Results[0].TrackGain, measured.TrackGain, 1e-9);
        Assert.IsNull(measured.AlbumGain);
        Assert.IsNotNull(scan.Results[0].AlbumGain);
        Assert.AreEqual(broken, progress.Reports.Single(report => report.Failure is not null).Failure!.Path);
    }

    [TestMethod]
    public async Task Scan_WithholdsAlbumGainWhenATrackOnTheAlbumFails()
    {
        // Arrange
        var good = MakeSine("incomplete-good.flac", -20, "-c:a flac");
        var broken = Path.Combine(folder, "incomplete-broken.flac");
        await File.WriteAllTextAsync(broken, "not audio");

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(good), new ReplayGainTrack(broken)], new ReplayGainOptions(SingleAlbum: true));

        // Assert: album gain from the remaining track alone would be wrong, so it keeps its track values only.
        Assert.AreEqual(1, scan.Failures.Count);
        Assert.IsNull(scan.Results.Single().AlbumGain);
        Assert.IsNull(scan.Results.Single().AlbumPeak);
    }

    [TestMethod]
    public async Task Scan_TakesTheFormatFromTheDecoder()
    {
        // Arrange: the same mono audio twice, once with an extension TagLib doesn't know, so only ffmpeg can say
        // it's mono. Measuring it as stereo would make it about 3 dB louder.
        var flac = MakeSine("mono.flac", -20, "-ac 1 -c:a flac");
        var unknown = Path.ChangeExtension(flac, ".audio");
        File.Copy(flac, unknown, overwrite: true);

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(flac), new ReplayGainTrack(unknown)], new ReplayGainOptions(AlbumGain: false));

        // Assert
        Assert.AreEqual(2, scan.Results.Count);
        Assert.AreEqual(scan.Results[0].Loudness, scan.Results[1].Loudness, 0.01);
    }

    [TestMethod]
    public async Task Scan_WeightsSurroundChannelsAndIgnoresTheLfe()
    {
        // Arrange: in 5.1, channel 0 is front left, 3 is LFE, and 4 is a surround channel.
        var front = MakeSurround("surround-front.flac", 0);
        var lfe = MakeSurround("surround-lfe.flac", 3);
        var surround = MakeSurround("surround-rear.flac", 4);

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(front), new ReplayGainTrack(lfe), new ReplayGainTrack(surround)],
            new ReplayGainOptions(AlbumGain: false));

        // Assert: BS.1770 weights surrounds 1.41, which is +1.49 dB, and leaves the LFE out.
        var loudness = scan.Results.ToDictionary(result => result.Path, result => result.Loudness);
        Assert.AreEqual(10 * Math.Log10(1.41), loudness[surround] - loudness[front], 0.02);
        Assert.IsTrue(double.IsNegativeInfinity(loudness[lfe]));
    }

    [TestMethod]
    public async Task WriteTags_FailsForFormatsThatCannotStoreReplayGain()
    {
        // Arrange: TagLib's Matroska tag, used for WebM, has no ReplayGain fields.
        var path = MakeSine("tags.webm", -20, "-c:a libopus");
        var options = new ReplayGainOptions(AlbumGain: false);
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(path)], options);
        var before = await File.ReadAllBytesAsync(path);

        // Act
        var failures = ReplayGainScanner.WriteTags(scan.Results, options);

        // Assert: reported as a failure, and the file is left as it was.
        Assert.AreEqual(1, failures.Count);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    public async Task WriteTags_StoresWavTagsAsId3v2()
    {
        // Arrange
        var path = MakeSine("tags.wav", -20, "-c:a pcm_s16le");
        var options = new ReplayGainOptions(AlbumGain: false);
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(path)], options);

        // Act
        var failures = ReplayGainScanner.WriteTags(scan.Results, options);

        // Assert
        Assert.AreEqual(0, failures.Count);
        Assert.AreEqual(scan.Results[0].TrackGain, ReplayGainScanner.ReadTags(path).TrackGain!.Value, 0.005);
    }

    [TestMethod]
    public async Task WriteTags_DoesNotAddAnId3v1TagToMp3()
    {
        // Arrange: ffmpeg writes only an ID3v2 tag, and TagLib would add an ID3v1 tag on save.
        var path = MakeSine("no-id3v1.mp3", -20, "-c:a libmp3lame -b:a 320k");
        var options = new ReplayGainOptions(AlbumGain: false);
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(path)], options);

        // Act
        ReplayGainScanner.WriteTags(scan.Results, options);

        // Assert
        using var file = TagLib.File.Create(path);
        Assert.IsFalse(file.TagTypesOnDisk.HasFlag(TagLib.TagTypes.Id3v1));
        Assert.IsTrue(file.TagTypesOnDisk.HasFlag(TagLib.TagTypes.Id3v2));
    }

    [TestMethod]
    public async Task Scan_ReturnsTheGainsTheFileHadBefore()
    {
        // Arrange: lower-case names and a plus sign, the way MusicBee writes FLAC tags.
        var path = MakeSine("existing.flac", -20, "-c:a flac -metadata replaygain_track_gain=\"+6.49 dB\" -metadata replaygain_track_peak=0.374664");
        var untagged = MakeSine("untagged.flac", -20, "-c:a flac");

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(path), new ReplayGainTrack(untagged)], new ReplayGainOptions());

        // Assert
        var existing = scan.Results.Single(result => result.Path == path).Existing!;
        Assert.AreEqual(6.49, existing.TrackGain!.Value, 1e-9);
        Assert.AreEqual(0.374664, existing.TrackPeak!.Value, 1e-9);
        Assert.IsNull(existing.AlbumGain);
        Assert.AreEqual(new ReplayGainTagValues(null, null, null, null), scan.Results.Single(result => result.Path == untagged).Existing);
    }

    [TestMethod]
    public async Task Scan_SkipsTracksThatAlreadyHaveTheTags()
    {
        // Arrange
        var tagged = MakeSine("skip-tagged.flac", -20, "-c:a flac");
        var untagged = MakeSine("skip-untagged.flac", -20, "-c:a flac");
        await ReplayGainScanner.ApplyTrackGainAsync(tagged, offset: 0, preventClipping: false);
        var progress = new CollectingProgress();

        // Act
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(tagged), new ReplayGainTrack(untagged)],
            new ReplayGainOptions(AlbumGain: false, SkipTaggedTracks: true), progress);

        // Assert
        CollectionAssert.AreEqual(new[] { tagged }, scan.Skipped.ToList());
        Assert.AreEqual(untagged, scan.Results.Single().Path);
        Assert.AreEqual(1, progress.Reports[0].Total);
    }

    [TestMethod]
    public async Task Scan_SkipsAnAlbumOnlyWhenEveryTrackHasTheTags()
    {
        // Arrange: the first track has album tags from being scanned alone; the second has none.
        const string album = "-c:a flac -metadata album=Skip -metadata album_artist=Artist";
        var first = MakeSine("album-skip-1.flac", -20, album);
        var second = MakeSine("album-skip-2.flac", -14, album);
        var alone = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(first)], new ReplayGainOptions());
        ReplayGainScanner.WriteTags(alone.Results, new ReplayGainOptions());
        var skipTagged = new ReplayGainOptions(SkipTaggedTracks: true);

        // Act
        var partlyTagged = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(first), new ReplayGainTrack(second)], skipTagged);
        ReplayGainScanner.WriteTags(partlyTagged.Results, new ReplayGainOptions());
        var fullyTagged = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(first), new ReplayGainTrack(second)], skipTagged);

        // Assert: the whole album is measured until both tracks have its tags, then the whole album is skipped.
        Assert.AreEqual(0, partlyTagged.Skipped.Count);
        Assert.AreEqual(2, partlyTagged.Results.Count);
        Assert.AreEqual(2, fullyTagged.Skipped.Count);
        Assert.AreEqual(0, fullyTagged.Results.Count);
    }

    [TestMethod]
    public async Task WriteTags_LeavesFilesThatAlreadyMatchUntouched()
    {
        // Arrange
        var path = MakeSine("unchanged.flac", -20, "-c:a flac");
        var options = new ReplayGainOptions(AlbumGain: false);
        ReplayGainScanner.WriteTags((await ReplayGainScanner.ScanAsync([new ReplayGainTrack(path)], options)).Results, options);
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        var again = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(path)], options);
        var shifted = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(path)], options with { Offset = -5 });

        // Act
        ReplayGainScanner.WriteTags(again.Results, options);

        // Assert
        Assert.IsTrue(ReplayGainScanner.HasTags(again.Results[0], options));
        Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(path));
        Assert.IsFalse(ReplayGainScanner.HasTags(shifted.Results[0], options with { Offset = -5 }));
    }

    [TestMethod]
    public async Task ReadTags_ReturnsWhatWriteTagsWrote()
    {
        // Arrange
        var path = MakeSine("read-back.mp3", -20, "-c:a libmp3lame -b:a 320k");
        var options = new ReplayGainOptions(SingleAlbum: true);
        var scan = await ReplayGainScanner.ScanAsync([new ReplayGainTrack(path)], options);
        ReplayGainScanner.WriteTags(scan.Results, options);

        // Act
        var tags = ReplayGainScanner.ReadTags(path);

        // Assert: tags store two decimals for gains and six for peaks.
        Assert.AreEqual(scan.Results[0].TrackGain, tags.TrackGain!.Value, 0.005);
        Assert.AreEqual(scan.Results[0].TrackPeak, tags.TrackPeak!.Value, 1e-6);
        Assert.AreEqual(scan.Results[0].AlbumGain!.Value, tags.AlbumGain!.Value, 0.005);
        Assert.AreEqual(scan.Results[0].AlbumPeak!.Value, tags.AlbumPeak!.Value, 1e-6);
    }

    // Progress<T> posts to a thread pool when there is no UI thread, so reports could arrive after
    // the scan returns. This one stores them as they're made.
    private sealed class CollectingProgress : IProgress<ReplayGainProgress>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<ReplayGainProgress> reports = new();

        public IReadOnlyList<ReplayGainProgress> Reports => reports.ToList();

        public void Report(ReplayGainProgress value) => reports.Enqueue(value);
    }
}
