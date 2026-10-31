using System.Globalization;
using FluentDL.Core.Sources;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class StatusMarkupTests
{
    [TestMethod]
    public void PublicTextStaysTextInsideAndOutsideLinks()
    {
        const string name = "x</a><a href='https://untrusted.example/'>y & z";
        var markup = StatusMarkup.Format($"<b>Provider</b> Loaded <a href='https://example.test/playlist'>{name}</a>. {name}");
        var parts = StatusMarkup.Parse(markup);
        Assert.AreEqual(1, parts.Count(part => part.Link != null));
        Assert.AreEqual(name, parts.Single(part => part.Link != null).Text);
        Assert.AreEqual("Provider Loaded " + name + ". " + name, string.Concat(parts.Select(part => part.Text)));
        Assert.IsTrue(parts[0].Bold);
    }

    [TestMethod]
    public void AttributeQuotesAndAmpersandsRoundTripWithoutCreatingAnotherLink()
    {
        const string url = "https://example.test/tracks?name=O'Reilly&quality=1";
        var parts = StatusMarkup.Parse(StatusMarkup.Format($"<a href='{url}'>A & B</a>"));
        Assert.AreEqual(1, parts.Count);
        Assert.AreEqual(url, parts[0].Link!.OriginalString);
        Assert.AreEqual("A & B", parts[0].Text);
    }

    [TestMethod]
    public void NumbersKeepTheirCultureAndFormat()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.AreEqual("Loaded 1,234 tracks", StatusMarkup.Format($"Loaded {1234:N0} tracks"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    [DataRow("x")]
    [DataRow("javascript:alert(1)")]
    [DataRow("ms-settings:privacy")]
    [DataRow("https://user:password@example.test/")]
    [DataRow("https://[invalid/")]
    [DataRow("//server/share/file.flac")]
    [DataRow("file:///C:/song.flac?option=value")]
    [DataRow("https://example.test/\nother")]
    public void InvalidOrUnexpectedTargetsArePlainText(string href)
    {
        var parts = StatusMarkup.Parse(StatusMarkup.Format($"<a href='{href}'>Track</a>"));
        Assert.AreEqual("Track", parts.Single().Text);
        Assert.IsNull(parts.Single().Link);
    }

    [TestMethod]
    [DataRow("https://example.test/")]
    [DataRow("http://example.test/")]
    [DataRow("fluentdl://settings/tidal")]
    [DataRow("C:\\Music\\O'Reilly & friends.flac")]
    [DataRow("\\\\unavailable-server.invalid\\music\\track.flac")]
    public void SupportedTargetsParseWithoutOpeningOrCheckingThem(string href) => Assert.IsNotNull(StatusMarkup.TryLink(href));

    [TestMethod]
    public void EncodedTagsAreDecodedOnlyAfterTokenization()
    {
        const string literal = "<b>not bold</b><a href='x'>not a link</a>";
        var parts = StatusMarkup.Parse(StatusMarkup.Text(literal));
        Assert.AreEqual(literal, parts.Single().Text);
        Assert.IsFalse(parts.Single().Bold);
        Assert.IsNull(parts.Single().Link);
    }
}
