using FluentDL.Core.Matching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

// Expected values come from the characterization baselines recorded on master, including the cases marked as bugs.
// Changing one of those is a behavior change: fix the bug in its own commit and update both test suites.
[TestClass]
public class TitleNormalizerTests
{
    [DataTestMethod]
    [DataRow("Get Lucky (feat. Pharrell Williams and Nile Rodgers)", "getlucky")]
    [DataRow("Get Lucky (ft. Pharrell)", "getlucky")]
    [DataRow("Stay (with Justin Bieber)", "stay")]
    [DataRow("Song (Featuring Someone)", "song")]
    [DataRow("Levels - Radio Edit", "levelsradioedit")]
    [DataRow("Yesterday [Remastered 2015]", "yesterdayremastered2015")]
    [DataRow("Beyonc\u00e9", "beyonce")]
    [DataRow("Mr. Brightside", "mrbrightside")]
    [DataRow("Don't Stop Me Now", "dontstopmenow")]
    [DataRow("Under \u2014 Pressure", "underpressure")]
    [DataRow("Unclosed (feat. Someone", "unclosedfeatsomeone")]
    public void Compact_RemovesFeaturingAndPunctuation(string title, string expected)
    {
        Assert.AreEqual(expected, TitleNormalizer.Compact(title));
    }

    [DataTestMethod]
    [DataRow("Get Lucky (feat. Pharrell Williams and Nile Rodgers)", "getlucky")]
    [DataRow("Stay (with Justin Bieber)", "stay")]
    [DataRow("Song (Featuring Someone)", "songfeaturingsomeone")]
    [DataRow("Beyonc\u00e9", "beyonce")]
    [DataRow("Under \u2014 Pressure", "underpressure")]
    public void CompactLegacy_RemovesOnlyFeatFtAndWithGroups(string title, string expected)
    {
        Assert.AreEqual(expected, TitleNormalizer.CompactLegacy(title));
    }

    [DataTestMethod]
    [DataRow("Get Lucky (feat. Pharrell Williams and Nile Rodgers)", "get lucky")]
    [DataRow("Stay (with Justin Bieber)", "stay (with justin bieber)")]
    [DataRow("Levels - Radio Edit", "levels")]
    [DataRow("Yesterday [Remastered 2015]", "yesterday remastered 2015")]
    [DataRow("Mr. Brightside", "mr. brightside")]
    [DataRow("Under \u2014 Pressure", "under pressure")]
    public void ForSearchQuery_KeepsWordsSeparated(string title, string expected)
    {
        Assert.AreEqual(expected, TitleNormalizer.ForSearchQuery(title));
    }

    // Bug 13 (fixed): every normalizer dropped non-ASCII characters, so any two Japanese titles compared equal ("").
    [TestMethod]
    public void NonLatinTitles_AreKept()
    {
        const string title = "\u591c\u306b\u99c6\u3051\u308b"; // 夜に駆ける

        Assert.AreEqual(title, TitleNormalizer.Compact(title));
        Assert.AreEqual(title, TitleNormalizer.CompactLegacy(title));
        Assert.AreEqual(title, TitleNormalizer.ForSearchQuery(title));
        Assert.AreEqual(title, TextNormalization.PrunePunctuation(title));
        Assert.AreNotEqual(TitleNormalizer.Compact(title), TitleNormalizer.Compact("\u30a2\u30a4\u30c9\u30eb")); // アイドル
        Assert.AreEqual("\u591c\u306b\u99c6\u3051\u308b", TitleNormalizer.Compact("\u591c\u306b\u99c6\u3051\u308b (feat. X)"));
    }

    [TestMethod]
    public void NonLatinNames_CanMatch()
    {
        Assert.IsTrue(FuzzyMatch.CloseMatch("\u7c73\u6d25\u7384\u5e2b", "\u7c73\u6d25\u7384\u5e2b")); // 米津玄師
        Assert.IsFalse(FuzzyMatch.CloseMatch("\u7c73\u6d25\u7384\u5e2b", "YOASOBI"));
    }

    // Bug 14 (fixed): an unclosed featuring bracket used to throw. It now runs to the end of the title.
    [DataTestMethod]
    [DataRow("Unclosed (feat. Someone", "unclosed")]
    [DataRow("Unclosed (ft. Someone", "unclosed")]
    [DataRow("Unclosed (with Someone", "unclosed")]
    [DataRow("Song (feat. A) (with B", "song")]
    public void CompactLegacy_RemovesAnUnclosedBracketToTheEnd(string title, string expected)
    {
        Assert.AreEqual(expected, TitleNormalizer.CompactLegacy(title));
    }

    // "(with" used to match the start of "(without", cutting it out of the title.
    [TestMethod]
    public void CompactLegacy_KeepsWithout()
    {
        Assert.AreEqual("lifewithoutyou", TitleNormalizer.CompactLegacy("Life (Without You)"));
        Assert.AreEqual("song", TitleNormalizer.CompactLegacy("Song (with Someone)"));
    }

    [TestMethod]
    public void ForSearchQuery_RemovesAnUnclosedFeatToTheEnd()
    {
        Assert.AreEqual("unclosed", TitleNormalizer.ForSearchQuery("Unclosed (feat. Someone"));
        Assert.AreEqual("unclosed", TitleNormalizer.ForSearchQuery("Unclosed (ft. Someone"));
        Assert.AreEqual("unclosed (with someone", TitleNormalizer.ForSearchQuery("Unclosed (with Someone"));
    }
}

[TestClass]
public class TextNormalizationTests
{
    [DataTestMethod]
    [DataRow("Get Lucky (feat. Pharrell Williams and Nile Rodgers)", "Get Lucky")]
    [DataRow("Song (Featuring Someone)", "Song")]
    [DataRow("Stay (with Justin Bieber)", "Stay")]
    [DataRow("Get Lucky (Radio Edit - feat. Pharrell Williams)", "Get Lucky (Radio Edit - feat. Pharrell Williams)")]
    [DataRow("   ", "")]
    [DataRow(null, "")]
    public void PruneFeaturing_RemovesBracketedCredits(string? title, string expected)
    {
        Assert.AreEqual(expected, TextNormalization.PruneFeaturing(title));
    }

    [DataTestMethod]
    [DataRow("Get Lucky (ft. Pharrell)", "GetLucky")]
    [DataRow("Don't Stop Me Now", "DontStopMeNow")]
    [DataRow("Beyonc\u00e9", "Beyonce")]
    [DataRow("Sigur R\u00f3s \u2014 Hopp\u00edpolla", "SigurRosHoppipolla")]
    [DataRow("\u591c\u306b\u99c6\u3051\u308b!", "\u591c\u306b\u99c6\u3051\u308b")]
    public void PrunePunctuation_KeepsLettersAndDigits(string text, string expected)
    {
        Assert.AreEqual(expected, TextNormalization.PrunePunctuation(text));
    }

    [DataTestMethod]
    [DataRow("Beyonc\u00e9", "Beyonce")]
    [DataRow("Sigur R\u00f3s", "Sigur Ros")]
    [DataRow("Under \u2014 Pressure", "Under  Pressure")]
    [DataRow("Don\u2019t", "Dont")]
    [DataRow("\u591c\u306b\u99c6\u3051\u308b", "\u591c\u306b\u99c6\u3051\u308b")]
    public void RemoveAccentsAndSymbols_KeepsAsciiAndLetters(string text, string expected)
    {
        Assert.AreEqual(expected, TextNormalization.RemoveAccentsAndSymbols(text));
    }
}

[TestClass]
public class FuzzyMatchTests
{
    [DataTestMethod]
    [DataRow("Get Lucky", "Get Lucky (feat. Pharrell Williams)", true)]
    [DataRow("Levels", "Levels (Skrillex Remix)", false)]
    [DataRow("Levels - Radio Edit", "Levels (Radio Edit)", true)]
    [DataRow("Black Eyed Peas", "The Black Eyed Peas", true)]
    [DataRow("Beyonc\u00e9", "Beyonce", true)]
    [DataRow("Halo", "Halo (Live)", false)]
    [DataRow("Random Access Memories", "Random Access Memories (10th Anniversary Edition)", false)]
    [DataRow("", "Something", false)]
    [DataRow("Daft Punk", "daft punk", true)]
    public void CloseMatch_IsSymmetricAndRespectsVersionKeywords(string a, string b, bool expected)
    {
        Assert.AreEqual(expected, FuzzyMatch.CloseMatch(a, b));
        Assert.AreEqual(expected, FuzzyMatch.CloseMatch(b, a));
    }

    // Bug 1: Deezer's conversion compared the song's artists with themselves, so this check always passed.
    [DataTestMethod]
    [DataRow("Daft Punk, Pharrell Williams, Nile Rodgers", "Daft Punk", true)]
    [DataRow("Pharrell Williams", "Daft Punk, Pharrell Williams", true)]
    [DataRow("Beyonc\u00e9", "Beyonce", true)]
    [DataRow("Thomas Bangalter", "Da Funk", false)]
    [DataRow("Daft Punk, Pharrell Williams", "Vitamin String Quartet", false)]
    public void AnyArtistMatches(string artists, string otherArtists, bool expected)
    {
        Assert.AreEqual(expected, FuzzyMatch.AnyArtistMatches(artists, otherArtists));
        Assert.AreEqual(expected, FuzzyMatch.AnyArtistMatches(otherArtists, artists));
    }

    [DataTestMethod]
    [DataRow("Levels", "Levels (Skrillex Remix)", true)]
    [DataRow("Halo", "Halo (Live)", true)]
    [DataRow("", "Something", false)]
    [DataRow("\u591c\u306b\u99c6\u3051\u308b", "Yoru ni Kakeru", false)]
    public void IsSubstring_IgnoresVersionKeywords(string a, string b, bool expected)
    {
        Assert.AreEqual(expected, FuzzyMatch.IsSubstring(a, b));
    }

    [DataTestMethod]
    [DataRow("Levels - Radio Edit", "Levels (Radio Edit)", 3)]
    [DataRow("Daft Punk", "daft punk", 2)]
    [DataRow("", "Something", 9)]
    [DataRow("", "", 0)]
    [DataRow("Halo", "Halo", 0)]
    public void LevenshteinDistance_CountsEdits(string a, string b, int expected)
    {
        Assert.AreEqual(expected, FuzzyMatch.LevenshteinDistance(a, b));
        Assert.AreEqual(expected, FuzzyMatch.LevenshteinDistance(b, a));
    }
}
