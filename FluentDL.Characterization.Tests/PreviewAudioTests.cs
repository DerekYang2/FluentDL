using System.Net.Http.Headers;
using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Contracts.Sources;
using FluentDL.Models;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// The preview pane plays a track's preview audio either from PreviewInfo.AudioUrl or, when LoadAudioSeparately is
/// set, from GetPreviewAudioAsync. The UI tests don't check audio, so this checks that every source gives one of the
/// two, and that the address serves audio.
/// </summary>
[TestClass]
[TestCategory("Live")]
public class PreviewAudioTests
{
    [TestMethod]
    [DataRow("deezer")]
    [DataRow("qobuz")]
    [DataRow("spotify")]
    [DataRow("tidal")]
    public Task TrackPreviewHasAudio(string source) => CheckAsync(source);

    // The refactor dropped LoadAudioSeparately from YouTube's preview, so the pane played nothing (fixed 2026-10-10).
    [TestMethod]
    [TestCategory("YouTube")]
    public Task YoutubeTrackPreviewHasAudio() => CheckAsync("youtube");

    private static async Task CheckAsync(string source)
    {
        var fixture = Fixtures.Tracks[0];
        SongSearchObject? song = source switch
        {
            "deezer" => await Signed(LiveSources.EnsureDeezerAsync, () => TestSources.Deezer.GetTrack(fixture.DeezerId!)),
            "qobuz" => await Signed(LiveSources.EnsureQobuzAsync, () => TestSources.Qobuz.GetTrackAsync(fixture.QobuzId!)),
            "spotify" => await Signed(LiveSources.EnsureSpotifyWebPlayerAsync, () => TestSources.Spotify.GetTrack(fixture.SpotifyId!)),
            "tidal" => await Signed(LiveSources.EnsureTidalAsync, () => TestSources.Tidal.GetTrackAsync("20115564")),
            _ => await TestSources.Youtube.GetTrack(fixture.YoutubeId!),
        };
        Assert.IsNotNull(song, "could not load the track");
        TestSources.Registry.TryGet<IPreviewSource>(source, out var preview);

        var info = await preview!.GetPreviewAsync(song);
        var audio = info.LoadAudioSeparately ? await preview.GetPreviewAudioAsync(song) : info.AudioUrl;

        Assert.IsFalse(string.IsNullOrEmpty(audio), $"no preview audio (AudioUrl {info.AudioUrl ?? "null"}, LoadAudioSeparately {info.LoadAudioSeparately})");
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, audio);
        request.Headers.Range = new RangeHeaderValue(0, 1023);
        using var response = await client.SendAsync(request);
        Assert.IsTrue(response.IsSuccessStatusCode, $"{(int)response.StatusCode} from {audio}");
        // TIDAL's preview is an HLS playlist (a text list of audio chunks), which the media player streams.
        var start = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.Content.Headers.ContentType?.MediaType?.StartsWith("audio/") == true || start.StartsWith("#EXTM3U"),
            $"neither audio nor an HLS playlist: {response.Content.Headers.ContentType}");
    }

    private static async Task<SongSearchObject?> Signed(Func<Task> signIn, Func<Task<SongSearchObject?>> load)
    {
        await signIn();
        return await load();
    }
}
