using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class ShellHelpMarkupTests
{
    private static XDocument Read(string name)
    {
        using var stream = typeof(ShellHelpMarkupTests).Assembly.GetManifestResourceStream(name)!;
        return XDocument.Load(stream);
    }

    [TestMethod]
    public void HelpIsAFooterActionAboveTheBuiltInSettingsItem()
    {
        var shell = Read("ShellPage.xaml");
        var navigation = shell.Descendants().Single(element => element.Name.LocalName == "NavigationView");
        Assert.AreEqual("True", (string?)navigation.Attribute("IsSettingsVisible"));
        var footer = navigation.Elements().Single(element => element.Name.LocalName == "NavigationView.FooterMenuItems");
        var help = footer.Elements().Single();
        Assert.AreEqual("Help", (string?)help.Attribute("Icon"));
        Assert.AreEqual("Right", (string?)help.Attribute("ToolTipService.Placement"));
        Assert.AreEqual("HelpNavigationItem", (string?)help.Attribute("AutomationProperties.AutomationId"));
    }

    [TestMethod]
    public void OpeningHelpCannotSelectOrNavigateToAnAppPage()
    {
        var shell = Read("ShellPage.xaml");
        var help = shell.Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "HelpNavigationItem");
        Assert.AreEqual("False", (string?)help.Attribute("SelectsOnInvoked"));
        Assert.IsFalse(help.Attributes().Any(attribute => attribute.Name.LocalName.Contains("NavigateTo")));
        var navigation = shell.Descendants().Single(element => element.Name.LocalName == "NavigationView");
        Assert.AreEqual("NavigationViewControl_ItemInvoked", (string?)navigation.Attribute("ItemInvoked"));
    }

    [TestMethod]
    public void HelpLabelsAndLaunchErrorsHaveLocalizedResources()
    {
        var resources = Read("AppResources.resw").Root!.Elements("data")
            .ToDictionary(element => (string)element.Attribute("name")!, element => (string?)element.Element("value"));
        foreach (var key in new[] { "Shell_Help.Content", "Shell_Help_Name", "Shell_Help_Tooltip", "Shell_Help_ErrorTitle", "Shell_Help_ErrorMessage", "Shell_Help_Close" })
            Assert.IsTrue(resources.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value), key);
        StringAssert.Contains(resources["Shell_Help_Tooltip"]!, "browser");
    }
}
