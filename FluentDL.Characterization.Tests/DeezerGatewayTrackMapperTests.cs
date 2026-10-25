using System.Text.Json;
using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class DeezerGatewayTrackMapperTests
{
    [TestMethod]
    public void DigitalDateAndOrderedUniqueArtistsPreserveTheIntendedMetadata()
    {
        Assert.IsTrue(DeezerGatewayTrackMapper.TryConvert(JsonSerializer.SerializeToElement(DeezerPlaylistImportTests.BatchTrack("4")), out var mapped));
        Assert.AreEqual("2024-01-02", mapped.GetProperty("release_date").GetString());
        CollectionAssert.AreEqual(new[] { "Artist A", "Artist B" },
            mapped.GetProperty("contributors").EnumerateArray().Select(artist => artist.GetProperty("name").GetString()).ToArray());
        Assert.AreEqual("USABC2400001", mapped.GetProperty("isrc").GetString());
        Assert.AreEqual("Fixture track (Remix)", mapped.GetProperty("title").GetString());
        Assert.AreEqual("2", mapped.GetProperty("track_position").GetString());
        Assert.AreEqual("239", mapped.GetProperty("duration").GetString());
        Assert.IsTrue(mapped.GetProperty("explicit_lyrics").GetBoolean());
        Assert.AreEqual("https://api.deezer.com/album/123/image", mapped.GetProperty("album").GetProperty("cover").GetString());
        Assert.AreEqual("https://cdnt-preview.dzcdn.net/preview.mp3?test=1", mapped.GetProperty("preview").GetString());
    }

    [TestMethod]
    [DataRow("ISRC")]
    [DataRow("DIGITAL_RELEASE_DATE")]
    [DataRow("ARTISTS")]
    [DataRow("ART_NAME")]
    [DataRow("DURATION")]
    [DataRow("ALB_ID")]
    [DataRow("ALB_TITLE")]
    [DataRow("TRACK_NUMBER")]
    [DataRow("RANK_SNG")]
    [DataRow("EXPLICIT_LYRICS")]
    [DataRow("MEDIA")]
    public void MissingFieldsRequireTheFullTrackFetchInsteadOfInventedDefaults(string field)
    {
        var record = DeezerPlaylistImportTests.BatchTrack("4");
        record.Remove(field);
        Assert.IsFalse(DeezerGatewayTrackMapper.TryConvert(JsonSerializer.SerializeToElement(record), out _));
    }

    [TestMethod]
    [DataRow("ISRC", "invalid")]
    [DataRow("DIGITAL_RELEASE_DATE", "2024-02-31")]
    [DataRow("DIGITAL_RELEASE_DATE", "")]
    [DataRow("EXPLICIT_LYRICS", "2")]
    [DataRow("DURATION", "0")]
    [DataRow("SNG_ID", "-1")]
    [DataRow("ALB_ID", "123/path")]
    public void AmbiguousOrInvalidValuesRequireTheFullTrackFetch(string field, string value)
    {
        var record = DeezerPlaylistImportTests.BatchTrack("4");
        record[field] = value;
        Assert.IsFalse(DeezerGatewayTrackMapper.TryConvert(JsonSerializer.SerializeToElement(record), out _));
    }

    [TestMethod]
    public void ExistingTitleVersionsAndUnavailablePreviewsAreHandledWithoutExtraRequests()
    {
        var record = DeezerPlaylistImportTests.BatchTrack("4");
        record["SNG_TITLE"] = "Fixture track (Remix)";
        record["MEDIA"] = Array.Empty<object>();
        Assert.IsTrue(DeezerGatewayTrackMapper.TryConvert(JsonSerializer.SerializeToElement(record), out var mapped));
        Assert.AreEqual("Fixture track (Remix)", mapped.GetProperty("title").GetString());
        Assert.AreEqual("", mapped.GetProperty("preview").GetString());
        record["MEDIA"] = new[] { new { HREF = "not a URL" } };
        Assert.IsFalse(DeezerGatewayTrackMapper.TryConvert(JsonSerializer.SerializeToElement(record), out _));
    }
}
