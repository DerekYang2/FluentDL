using FluentDL.Services.Sources;

namespace FluentDL.Characterization.Tests;

/// <summary>
/// Bug 16: YouTube answers some YouTube Music song detail requests with a bot check and no details, at random, and the
/// library throws ArgumentNullException("videoDetails.title"). YoutubeSource retries those. No network.
/// </summary>
[TestClass]
public class YoutubeRetryTests
{
    private static ArgumentNullException BotCheck() => new("videoDetails.title", "Requiered token is null.");

    [TestMethod]
    public async Task ABotCheckIsRetried()
    {
        var calls = 0;
        var result = await YoutubeSource.RetryBotCheckAsync(() => ++calls < 3 ? throw BotCheck() : Task.FromResult("details"));

        Assert.AreEqual("details", result);
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    public async Task AfterThreeBotChecksTheErrorIsThrown()
    {
        var calls = 0;
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => YoutubeSource.RetryBotCheckAsync<string>(() => { calls++; throw BotCheck(); }));
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    public async Task OtherErrorsAreNotRetried()
    {
        var calls = 0;
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => YoutubeSource.RetryBotCheckAsync<string>(() => { calls++; throw new HttpRequestException("offline"); }));
        Assert.AreEqual(1, calls);
    }
}
