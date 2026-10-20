using FluentDL.Characterization.Tests.Infrastructure;
using FluentDL.Core.Sources;
using FluentDL.Services.Sources;
using FluentDL.Services.Tidal;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace FluentDL.Characterization.Tests;

[TestClass]
[TestCategory("Live")]
public class TidalSignedInPreviewTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SignedInPreviewOpensInWindowsWithoutPlayingAudioOrChangingTheSession(bool bundledCredentials)
    {
        var path = Environment.GetEnvironmentVariable("FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION");
        if (string.IsNullOrEmpty(path)) Assert.Inconclusive("Set FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION for this read-only playback check.");
        var saved = Convert.ToBase64String(await File.ReadAllBytesAsync(path!));
        var settings = new TidalParsingTests.DictionarySettings(new()
        {
            [SettingKeys.TidalDownloadSession] = saved,
            [SettingKeys.TidalCountry] = "US",
        });
        if (!bundledCredentials)
        {
            await settings.SaveSettingAsync(SettingKeys.TidalClientId, Secrets.Require("tidal_client_id"));
            await settings.SaveSettingAsync(SettingKeys.TidalClientSecret, Secrets.Require("tidal_client_secret"));
        }
        using var http = new HttpClient();
        var api = new TidalApi(settings, http);
        if (bundledCredentials && (api.BuiltInClientId == null || api.BuiltInClientSecret == null))
            Assert.Inconclusive("This build has no bundled TIDAL preview credentials.");
        var source = new TidalSource(api);
        var address = await source.GetPreviewAudioAsync(new() { Source = SourceIds.Tidal, Id = "20115564" });
        Assert.IsFalse(string.IsNullOrWhiteSpace(address));

        using var media = MediaSource.CreateFromUri(new Uri(address!));
        using var player = new MediaPlayer { AutoPlay = false, IsMuted = true };
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened += (_, _) => opened.TrySetResult();
        player.MediaFailed += (_, error) => opened.TrySetException(new InvalidOperationException($"Preview failed: {error.Error}, HRESULT {error.ExtendedErrorCode?.HResult:X8}"));
        player.Source = media;
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsTrue(player.PlaybackSession.NaturalDuration.TotalSeconds is > 0 and <= 35, "The player must open a preview, not the full track.");
        Assert.AreNotEqual(MediaPlaybackState.Playing, player.PlaybackSession.PlaybackState);
        Assert.AreEqual(saved, await settings.ReadSettingAsync<string>(SettingKeys.TidalDownloadSession));
        Assert.AreEqual(TidalAccess.None, api.Access, "Preview loading must not mutate catalog authentication.");
        player.Source = null;
        Console.WriteLine("Signed-in settings produced a playable preview under 35 seconds. No audio played and the saved session was unchanged.");
    }
}
