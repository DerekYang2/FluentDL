using FluentDL.Services;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class YtDlpFormatTests
{
    [TestMethod]
    [DataRow(true, "worstaudio")]
    [DataRow(false, "bestaudio")]
    public void PrefersOpusWithAnAacFallback(bool worst, string audio)
    {
        CollectionAssert.AreEqual(
            new[] { $"{audio}[acodec=opus]", $"{audio}[acodec^=opus]", $"{audio}[acodec^=mp4a]" },
            YtDlp.PreferredAudioFormat(worst).Split('/'));
    }
}
