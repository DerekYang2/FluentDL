using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class UpdateMarkupTests
{
    private static string Read(string resource)
    {
        using var stream = typeof(UpdateMarkupTests).Assembly.GetManifestResourceStream(resource);
        Assert.IsNotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [TestMethod]
    public void ServiceAccessHeaderAndRowsKeepTheWiderServiceColumnAligned()
    {
        var markup = XDocument.Parse(Read("SettingsPage.xaml"));
        var card = markup.Descendants().Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "ServiceSupportCard"));
        var grids = card.Descendants().Where(element => element.Elements().Any(child => child.Name.LocalName == "Grid.ColumnDefinitions")).ToArray();
        Assert.AreEqual(2, grids.Length);
        foreach (var grid in grids)
        {
            CollectionAssert.AreEqual(new[] { "140", "140", "164", "184", "140" },
                grid.Descendants().Where(element => element.Name.LocalName == "ColumnDefinition").Select(element => (string?)element.Attribute("Width")).ToArray());
            Assert.AreEqual("12", (string?)grid.Attribute("ColumnSpacing"));
        }
        Assert.AreEqual("832", (string?)card.Descendants().Single(element => element.Attribute("MinWidth") != null).Attribute("MinWidth"));
    }

    [TestMethod]
    public void UpdateActionsAreExplicitAndThePremiumReminderIsNotDuplicated()
    {
        var markup = XDocument.Parse(Read("SettingsPage.xaml"));
        foreach (var id in new[] { "CheckAppUpdate", "AppUpdateAction", "CancelAppUpdate", "AppUpdateSourceLink" })
            Assert.AreEqual(1, markup.Descendants().Count(element => (string?)element.Attribute("AutomationProperties.AutomationId") == id));
        Assert.IsFalse(markup.Descendants().Any(element => ((string?)element.Attribute("Text"))?.Contains("The app owner needs Spotify Premium.") == true));
    }

    [TestMethod]
    public void ElevatedHelperVerifiesTheOfficialArchiveAndWaitsForNormalExit()
    {
        var helper = Read("InstallUpdate.ps1");
        StringAssert.Contains(helper, "https://api.github.com/repos/DerekYang2/FluentDL/releases/tags/");
        StringAssert.Contains(helper, "[IO.FileShare]::Read");
        StringAssert.Contains(helper, "ComputeHash($stream)");
        StringAssert.Contains(helper, "$source.WaitForExit");
        Assert.IsFalse(helper.Contains("ForceApplicationShutdown"));
        Assert.IsFalse(helper.Contains("Invoke-Expression"));
        Assert.IsFalse(helper.Contains("Stop-Process"));
    }

    [TestMethod]
    public void StorePackagesDoNotShipTheElevatedSideloadInstaller()
    {
        var project = XDocument.Parse(Read("UpdaterProject.xml"));
        var item = project.Descendants("Content").Single(element => ((string?)element.Attribute("Include"))?.EndsWith("InstallUpdate.ps1") == true);
        Assert.AreEqual("'$(FluentDLPackageChannel)' != 'Store'", (string?)item.Attribute("Condition"));
        Assert.IsTrue(project.Descendants("Content").Any(element =>
            (string?)element.Attribute("Remove") == "Assets\\Updater\\**"
            && (string?)element.Attribute("Condition") == "'$(FluentDLPackageChannel)' == 'Store'"));
    }
}
