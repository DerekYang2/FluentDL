using FluentDL.Views;

namespace FluentDL.Characterization.Tests;

[TestClass]
public class ShellHelpTests
{
    [TestMethod]
    public void HelpUsesTheExistingProjectWikiOverHttps()
    {
        Assert.AreEqual("https", ShellPage.HelpUri.Scheme);
        Assert.AreEqual("github.com", ShellPage.HelpUri.Host);
        Assert.AreEqual("/DerekYang2/FluentDL/wiki", ShellPage.HelpUri.AbsolutePath);
        Assert.AreEqual(string.Empty, ShellPage.HelpUri.Query);
    }
}
