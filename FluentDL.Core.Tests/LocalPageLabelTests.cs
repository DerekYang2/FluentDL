using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class LocalPageLabelTests
{
    [TestMethod]
    public void NavigationUsesLocalWithoutChangingItsResourceKey()
    {
        var resources = Read("AppResources.resw");
        Assert.AreEqual("Local", resources.Descendants("data")
            .Single(element => (string?)element.Attribute("name") == "Shell_LocalExplorer.Content").Element("value")?.Value);
    }

    [TestMethod]
    public void SettingsAndQueueReferToTheSamePageName()
    {
        var settings = Read("SettingsPage.xaml").Descendants().Single(element =>
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "LocalExplorerPageShortcuts"));
        Assert.AreEqual("Local page", (string?)settings.Attribute("Header"));
        Assert.AreEqual("Enabled shortcut buttons for Local", (string?)settings.Attribute("Description"));
        Assert.IsTrue(Read("QueuePage.xaml").Descendants().Any(element =>
            (string?)element.Attribute("Text") == "Add tracks through Search or Local."));
    }

    private static XDocument Read(string name)
    {
        using var stream = typeof(LocalPageLabelTests).Assembly.GetManifestResourceStream(name);
        Assert.IsNotNull(stream);
        return XDocument.Load(stream);
    }
}
