using FluentDL.Core.Sources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class SourceServicesTests
{
    // Users' saved settings use these keys, so they must not change.
    [TestMethod]
    public void SettingKeys_KeepTheSavedNames()
    {
        Assert.AreEqual("deezer_quality", SettingKeys.DeezerQuality);
        Assert.AreEqual("qobuz_quality", SettingKeys.QobuzQuality);
        Assert.AreEqual("spotify_quality", SettingKeys.SpotifyQuality);
        Assert.AreEqual("overwrite", SettingKeys.Overwrite);
        Assert.AreEqual("deezer_arl", SettingKeys.DeezerArl);
        Assert.AreEqual("qobuz_id", SettingKeys.QobuzId);
        Assert.AreEqual("qobuz_token", SettingKeys.QobuzToken);
        Assert.AreEqual("qobuz_app_id", SettingKeys.QobuzAppId);
        Assert.AreEqual("qobuz_app_secret", SettingKeys.QobuzAppSecret);
        Assert.AreEqual("spotify_client_Id", SettingKeys.SpotifyClientId); // capital I, matching users' saved settings
        Assert.AreEqual("spotify_client_secret", SettingKeys.SpotifyClientSecret);
        Assert.AreEqual("spotify_playlist_prompt", SettingKeys.SpotifyPlaylistPrompt);
        Assert.AreEqual("youtube_quality", SettingKeys.YoutubeQuality);
        Assert.AreEqual("use_ytdlp", SettingKeys.UseYtdlp);
        Assert.AreEqual("ytdlp_path", SettingKeys.YtdlpPath);
    }

    // The app maps StatusLevel to WinUI's InfoBarSeverity, which has the same members in the same order.
    [TestMethod]
    public void StatusLevel_MatchesInfoBarSeverity()
    {
        CollectionAssert.AreEqual(
            new[] { "Informational", "Success", "Warning", "Error" },
            Enum.GetNames<StatusLevel>());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, Enum.GetValues<StatusLevel>().Select(l => (int)l).ToArray());
    }
}
