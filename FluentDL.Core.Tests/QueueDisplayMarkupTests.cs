using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class QueueDisplayMarkupTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument ReadMarkup(string name)
    {
        using var stream = typeof(QueueDisplayMarkupTests).Assembly.GetManifestResourceStream(name);
        Assert.IsNotNull(stream);
        return XDocument.Load(stream);
    }

    [TestMethod]
    public void CompactTemplate_HasSmallerRows_AndColumnsMatchItsHeader()
    {
        // Arrange
        var page = ReadMarkup("QueuePage.xaml");
        var compact = page.Descendants(Xaml + "DataTemplate").Single(element => (string?)element.Attribute(X + "Key") == "CompactQueueItemTemplate");
        var list = page.Descendants(Xaml + "ListView").Single(element => (string?)element.Attribute(X + "Name") == "CustomListView");
        var regular = list.Element(Xaml + "ListView.ItemTemplate")!.Element(Xaml + "DataTemplate")!.Element(Xaml + "Grid")!;
        var header = list.Element(Xaml + "ListView.Header")!.Element(Xaml + "Grid")!;
        var containerStyle = page.Descendants(Xaml + "Style").Single(element => (string?)element.Attribute(X + "Key") == "CompactQueueItemStyle");

        // Act
        var row = compact.Element(Xaml + "Grid")!;
        var rowColumns = row.Element(Xaml + "Grid.ColumnDefinitions")!.Elements().Select(element => (string?)element.Attribute("Width")).ToArray();
        var headerColumns = header.Element(Xaml + "Grid.ColumnDefinitions")!.Elements().Select(element => (string?)element.Attribute("Width")).ToArray();
        var headings = header.Elements(Xaml + "TextBlock").Select(element => (string?)element.Attribute("Text")).ToArray();

        // Assert
        Assert.AreEqual("44", (string?)row.Attribute("MinHeight"));
        Assert.AreEqual("0,6,0,6", (string?)row.Attribute("Margin"));
        Assert.AreEqual("12,0", (string?)containerStyle.Elements(Xaml + "Setter")
            .Single(element => (string?)element.Attribute("Property") == "Padding").Attribute("Value"));
        Assert.IsNull(row.Attribute("Height"), "Compact rows must be able to grow for larger text.");
        Assert.AreEqual("76", (string?)regular.Attribute("Height"));
        Assert.AreEqual("0,12,0,12", (string?)regular.Attribute("Margin"));
        CollectionAssert.AreEqual(rowColumns, headerColumns);
        CollectionAssert.AreEqual(new[] { "Title", "Album", "Year", "Source", "Duration" }, headings);
        Assert.IsTrue(double.Parse(rowColumns[^1]!, System.Globalization.CultureInfo.InvariantCulture) >= 32 + 24 + 8,
            "The status column must fit the output button and progress ring simultaneously.");
        Assert.AreEqual((string?)row.Attribute("SizeChanged"), (string?)header.Attribute("SizeChanged"));
        StringAssert.Contains((string)header.Attribute("Visibility")!, "ViewModel.QueueDisplay.IsCompact, Mode=OneWay");
        Assert.IsTrue(row.Descendants(Xaml + "TextBlock").Any(element => (string?)element.Attribute("Text") == "{x:Bind AlbumName}"));
    }

    [TestMethod]
    public void CompactTemplate_HidesInlineShortcuts_ButKeepsContextActionsAndLiveStatus()
    {
        // Arrange
        var page = ReadMarkup("QueuePage.xaml");
        var compact = page.Descendants(Xaml + "DataTemplate").Single(element => (string?)element.Attribute(X + "Key") == "CompactQueueItemTemplate");

        // Act
        var output = compact.Descendants(Xaml + "Button").Single();
        var progress = compact.Descendants(Xaml + "ProgressRing").Single();
        var badge = compact.Descendants(Xaml + "InfoBadge").Single();
        var commands = compact.Descendants(Xaml + "MenuFlyoutItem").Select(element => (string?)element.Attribute("Click")).ToArray();

        // Assert
        Assert.AreEqual("OutputButton_OnClick", (string?)output.Attribute("Click"));
        StringAssert.Contains((string)output.Attribute("Visibility")!, "ResultString, Mode=OneWay");
        Assert.AreEqual("{x:Bind IsRunning, Mode=OneWay}", (string?)progress.Attribute("IsActive"));
        StringAssert.Contains((string)progress.Attribute("Visibility")!, "IsRunning, Mode=OneWay");
        Assert.AreEqual("{x:Bind ConvertBadgeColor, Mode=OneWay}", (string?)badge.Attribute("Background"));
        CollectionAssert.AreEquivalent(new[]
        {
            "ShareLinkButton_OnClick", "DownloadCoverButton_OnClick", "RemoveButton_OnClick",
            "OpenSpekButton_Click", "OpenLocalButton_Click"
        }, commands);
        Assert.IsFalse(compact.Descendants().Any(element => element.Name.LocalName == "MarqueeText"));
    }

    [TestMethod]
    public void BothToggles_ObserveTheSamePreference_AndDisableDuringUpdates()
    {
        // Arrange
        var queue = ReadMarkup("QueuePage.xaml");
        var settings = ReadMarkup("SettingsPage.xaml");

        // Act
        var button = queue.Descendants(Xaml + "ToggleButton").Single(element => (string?)element.Attribute(X + "Name") == "CompactQueueButton");
        var toggle = settings.Descendants(Xaml + "ToggleSwitch").Single(element => (string?)element.Attribute(X + "Name") == "CompactQueueToggle");

        // Assert
        Assert.AreEqual("{x:Bind ViewModel.QueueDisplay.IsCompact, Mode=OneWay}", (string?)button.Attribute("IsChecked"));
        Assert.AreEqual((string?)button.Attribute("IsChecked"), (string?)toggle.Attribute("IsOn"));
        Assert.AreEqual("{x:Bind ViewModel.QueueDisplay.CanChangeLayout, Mode=OneWay}", (string?)button.Attribute("IsEnabled"));
        Assert.AreEqual((string?)button.Attribute("IsEnabled"), (string?)toggle.Attribute("IsEnabled"));
        Assert.IsNotNull(button.Attribute("AutomationProperties.Name"));
        Assert.IsNotNull(toggle.Attribute("AutomationProperties.Name"));
    }
}
