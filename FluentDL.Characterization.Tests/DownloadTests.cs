using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// The download pipeline (DownloadService.DownloadObject and DownloadAlbum): one fixture per source, downloaded into a fresh folder with the
/// settings a new install has. Pins the reported result, the files written, and the tags in the audio file.
///
/// Free accounts shape these: Deezer falls back to a 128 kbps MP3, and Qobuz serves a 30-second MP3 sample. FFmpeg
/// isn't set up in the test host, so branches that convert audio take their "no FFmpeg" path. YouTube is left out
/// (slow, and it bot-checks heavy use).
/// </summary>
[TestClass]
[TestCategory("Live")]
[TestCategory("Download")]
public class DownloadTests
{
    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    public async Task DownloadTrack(string source)
    {
        await SignInAsync(source);
        var fixture = Fixtures.Tracks[0];

        await Snapshot.MatchLive($"Download.{source}", async () =>
        {
            var song = await LoadAsync(source, fixture);
            if (song == null)
            {
                return "could not load the track";
            }

            var directory = Path.Combine(Path.GetTempPath(), "opencode", "fluentdl-downloads", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var reports = new List<object>();
                void Report(StatusLevel level, SongSearchObject reported, string? location) =>
                    reports.Add(new { Level = level.ToString(), Song = reported.Id, Location = Relative(directory, location) });

                var result = await Live.Capture(async () =>
                    Relative(directory, await TestSources.Downloads.DownloadObject(song, directory, new Progress<ProgressData>(_ => { }), Report)));

                return new
                {
                    Result = result,
                    Reports = reports,
                    Files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(f => Describe(directory, f)).OrderBy(f => f.Name).ToArray(),
                };
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            }
        });
    }

    // A whole album: about 80 MB from Deezer (13 tracks at 128 kbps) and 13 samples from Qobuz. No Spotify album: in web
    // player mode, album links load nothing.
    [TestMethod]
    [TestCategory("Slow")]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    public async Task DownloadAlbum(string source)
    {
        await SignInAsync(source);
        var url = source == "deezer" ? $"https://www.deezer.com/album/{Fixtures.DeezerAlbumId}" : $"https://open.qobuz.com/album/{Fixtures.QobuzAlbumId}";

        await Snapshot.MatchLive($"DownloadAlbum.{source}", async () =>
        {
            var items = new List<SongSearchObject>();
            var linkSource = source == "deezer" ? (Contracts.Sources.ILinkSource)TestSources.Deezer : TestSources.Qobuz;
            await linkSource.AddFromLinkAsync(items, url, new(IsFirstPage: true, new(0, 0), AlbumMode: true), null, CancellationToken.None);
            if (items is not [AlbumSearchObject album])
            {
                return $"the album link loaded {items.Count} items";
            }

            var directory = Path.Combine(Path.GetTempPath(), "opencode", "fluentdl-downloads", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var reports = new List<object>();
                void Report(StatusLevel level, SongSearchObject reported, string? location)
                {
                    lock (reports)
                    {
                        reports.Add(new { Level = level.ToString(), Song = reported.Id, Location = Relative(directory, location) });
                    }
                }

                var result = await Live.Capture(async () =>
                    (await TestSources.Downloads.DownloadAlbum(album, directory, new Progress<ProgressData>(_ => { }), Report))
                        .Select(p => Relative(directory, p)).OrderBy(p => p).ToArray());

                // The cover is saved by a task that DownloadAlbum doesn't wait for.
                WaitForQuietFolder(directory);
                return new
                {
                    Result = result,
                    Reports = reports.OrderBy(r => r.ToString()).ToArray(),
                    Files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(f => Describe(directory, f)).OrderBy(f => f.Name).ToArray(),
                };
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            }
        });
    }

    // Waits until no file has been added or changed for 3 s, or 30 s in total.
    private static void WaitForQuietFolder(string directory)
    {
        string State() => string.Join("|", Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Select(f => f + ":" + new FileInfo(f).Length));
        var last = State();
        var quietSince = DateTime.UtcNow;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && DateTime.UtcNow - quietSince < TimeSpan.FromSeconds(3))
        {
            Thread.Sleep(250);
            var now = State();
            if (now != last)
            {
                last = now;
                quietSince = DateTime.UtcNow;
            }
        }
    }

    private static Task SignInAsync(string source) => source switch
    {
        "deezer" => LiveSources.EnsureDeezerAsync(),
        "qobuz" => LiveSources.EnsureQobuzAsync(),
        // Spotify downloads go through an equivalent on Deezer, then Qobuz.
        _ => Task.WhenAll(LiveSources.EnsureSpotifyWebPlayerAsync(), LiveSources.EnsureDeezerAsync(), LiveSources.EnsureQobuzAsync()),
    };

    private static async Task<SongSearchObject?> LoadAsync(string source, Fixtures.Track fixture) => source switch
    {
        "deezer" => await TestSources.Deezer.GetTrack(fixture.DeezerId!),
        "qobuz" => await TestSources.Qobuz.GetTrackAsync(fixture.QobuzId!),
        _ => await SpotifyApi.GetTrack(fixture.SpotifyId!),
    };

    // Error results carry a message instead of a path; only paths inside the folder are rewritten.
    private static string? Relative(string directory, string? path) =>
        path != null && path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(directory, path) : path;

    // Size in a coarse band, because the same track can be re-encoded by the service.
    private static string SizeBand(long bytes) => bytes switch
    {
        < 100_000 => "under 100 KB",
        < 1_000_000 => "100 KB to 1 MB",
        < 5_000_000 => "1 to 5 MB",
        < 20_000_000 => "5 to 20 MB",
        _ => "over 20 MB",
    };

    private static FileFacts Describe(string directory, string file)
    {
        var facts = new FileFacts(Path.GetRelativePath(directory, file), SizeBand(new FileInfo(file).Length), null);
        try
        {
            using var tagFile = TagLib.File.Create(file);
            var tag = tagFile.Tag;
            return facts with
            {
                Tags = new
                {
                    tag.Title,
                    Performers = tag.Performers,
                    AlbumArtists = tag.AlbumArtists,
                    tag.Album,
                    tag.Track,
                    tag.TrackCount,
                    tag.Disc,
                    tag.Year,
                    tag.ISRC,
                    Genres = tag.Genres,
                    HasCover = tag.Pictures.Length > 0,
                    DurationSeconds = (int)Math.Round(tagFile.Properties.Duration.TotalSeconds),
                    tagFile.MimeType,
                },
            };
        }
        catch (Exception e) when (e is TagLib.UnsupportedFormatException or TagLib.CorruptFileException)
        {
            return facts; // not an audio file, such as a cover image or lyrics
        }
    }

    private sealed record FileFacts(string Name, string Size, object? Tags);
}
