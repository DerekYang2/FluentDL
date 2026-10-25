using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class CompactLocalMarkupTests
{
    [TestMethod]
    public void LocalKeepsItsVirtualizedListAndBindsTheSavedLayoutToggle()
    {
        var page = Read("LocalExplorerPage.xaml");
        var list = Named(page, "FileListView");
        Assert.AreEqual("ListView", list.Name.LocalName);
        Assert.AreEqual("True", (string?)list.Attribute("ShowsScrollingPlaceholders"));
        Assert.IsFalse(list.Ancestors().Any(element => element.Name.LocalName == "ScrollViewer"));
        var toggle = Named(page, "CompactLocalButton");
        StringAssert.Contains((string)toggle.Attribute("IsChecked")!, "ViewModel.LocalDisplay.IsCompact, Mode=OneWay");
        Assert.AreEqual("CompactLocalButton_Click", (string?)toggle.Attribute("Click"));
    }

    [TestMethod]
    public void CompactLocalRowsHaveNoQueueStatusOrInlineActionButton()
    {
        var row = Read("CompactLocalRow.xaml");
        Assert.AreEqual(1, row.Descendants().Count(element => element.Name.LocalName == "CompactTrackIdentity"));
        Assert.IsFalse(row.Descendants().Any(element => element.Name.LocalName == "Button"));
        Assert.IsFalse(row.ToString().Contains("IsRunning"));
        StringAssert.Contains(row.ToString(), "Item.AudioFormat, Mode=OneWay");
    }

    [TestMethod]
    public void LocalAndQueueConversionSummariesUseTheSameCompactResultControl()
    {
        foreach (var file in new[] { "LocalExplorerPage.xaml", "QueuePage.xaml" })
        {
            var list = Named(Read(file), "ConversionListView");
            Assert.AreEqual("{StaticResource CompactTrackListItemStyle}", (string?)list.Attribute("ItemContainerStyle"));
            Assert.AreEqual(1, list.Descendants().Count(element => element.Name.LocalName == "CompactResultRow"));
            Assert.IsFalse(list.Descendants().Any(element => (string?)element.Attribute("Height") == "76"));
        }
        var localRow = Named(Read("LocalExplorerPage.xaml"), "ConversionListView").Descendants()
            .Single(element => element.Name.LocalName == "CompactResultRow");
        Assert.AreEqual("OpenConversionButton_OnClick", (string?)localRow.Attribute("OpenClick"));
        StringAssert.Contains((string)localRow.Attribute("FailureVisibility")!, "NewPath");
    }

    [TestMethod]
    public void CompactResultsShowAlbumTrackCountInsteadOfReleaseYear()
    {
        var row = Read("CompactResultRow.xaml");
        Assert.IsFalse(row.ToString().Contains("ReleaseDate"));
        var count = row.Descendants().Single(element =>
            (string?)element.Attribute("AutomationProperties.AutomationId") == "CompactResultTrackCount");
        Assert.AreEqual("{x:Bind views:CompactResultRow.TrackCount(Item), Mode=OneWay}", (string?)count.Attribute("Text"));
        Assert.IsTrue(row.Descendants().Any(element => ((string?)element.Attribute("Text"))?.Contains("Item.Duration") == true));
    }

    [TestMethod]
    public void CompactIdentityRecyclesBindingsAndUsesExistingArtwork()
    {
        var identity = Read("CompactTrackIdentity.xaml");
        Assert.IsTrue(identity.Descendants().Any(element => (string?)element.Attribute("Width") == "44" && (string?)element.Attribute("Height") == "44"));
        var image = identity.Descendants().Single(element => element.Name.LocalName == "QueueArtworkTile");
        Assert.AreEqual("{x:Bind Item, Mode=OneWay}", (string?)image.Attribute("Item"));
        StringAssert.Contains((string)image.Attribute("UseUrlFallback")!, "CachedArtworkOnly");
        StringAssert.Contains((string)image.Attribute("UseUrlFallback")!, "Mode=OneWay");
        var style = Read("AppStyles.xaml").Descendants().Single(element => element.Attributes().Any(attribute =>
            attribute.Name.LocalName == "Key" && attribute.Value == "CompactTrackListItemStyle"));
        Assert.IsTrue(style.Elements().Any(element => (string?)element.Attribute("Property") == "MinHeight" && (string?)element.Attribute("Value") == "0"));
    }

    private static XElement Named(XDocument document, string name) => document.Descendants().Single(element =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));
    private static XDocument Read(string name)
    {
        using var stream = typeof(CompactLocalMarkupTests).Assembly.GetManifestResourceStream(name);
        Assert.IsNotNull(stream);
        return XDocument.Load(stream);
    }
}
