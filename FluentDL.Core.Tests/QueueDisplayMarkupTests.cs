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
        CollectionAssert.AreEqual(new[] { "Title", "Album", "Year", "Duration", "Source" }, headings);
        Assert.AreEqual("80", rowColumns[4], "Clock durations need room for hours as well as minutes.");
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
        Assert.AreEqual(2, compact.Descendants().Count(element => element.Name.LocalName == "MarqueeText"));
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
        Assert.AreEqual("List", (string?)button.Descendants(Xaml + "TextBlock").Single().Attribute("Text"));
        Assert.AreEqual(1, button.Descendants(Xaml + "FontIcon").Count());
    }

    [TestMethod]
    public void CompactTemplate_MarqueesLikeSearch_AndPlacesExplicitBeforeArtists()
    {
        // Arrange
        var queue = ReadMarkup("QueuePage.xaml");
        var search = ReadMarkup("Search.xaml");
        var compact = queue.Descendants(Xaml + "DataTemplate").Single(element => (string?)element.Attribute(X + "Key") == "CompactQueueItemTemplate");

        // Act
        var marquees = compact.Descendants().Where(element => element.Name.LocalName == "MarqueeText").ToArray();
        var artists = marquees.Single(element => (string?)element.Attribute("Text") == "{x:Bind Artists, Mode=OneWay}");
        var artistRow = artists.Parent!;
        var explicitBadge = artistRow.Element(Xaml + "Border")!;

        // Assert
        foreach (var field in new[] { "Title", "Artists" })
        {
            var queueField = marquees.Single(element => (string?)element.Attribute("Text") == $"{{x:Bind {field}, Mode=OneWay}}");
            var searchField = search.Descendants().Single(element => element.Name.LocalName == "MarqueeText"
                && (string?)element.Attribute("Text") == $"{{x:Bind {field}}}");
            foreach (var property in new[] { "Behavior", "Direction", "RepeatBehavior", "FontFamily" })
            {
                Assert.AreEqual((string?)searchField.Attribute(property), (string?)queueField.Attribute(property));
            }
        }
        Assert.AreEqual("1", (string?)artistRow.Attribute("Grid.Row"));
        Assert.AreEqual("1", (string?)artists.Attribute("Grid.Column"));
        Assert.AreEqual("0", (string?)explicitBadge.Attribute("Grid.Column") ?? "0");
        StringAssert.Contains((string)explicitBadge.Attribute("Visibility")!, "Explicit, Mode=OneWay");
        CollectionAssert.AreEqual(new[] { "Auto", "*" },
            artistRow.Element(Xaml + "Grid.ColumnDefinitions")!.Elements().Select(element => (string?)element.Attribute("Width")).ToArray());
        Assert.IsNull(artistRow.Attribute("ColumnSpacing"), "A hidden explicit badge must not leave an empty gap.");
    }

    [TestMethod]
    public void CompactTemplate_UsesClockDurationBeforeSource_WithoutChangingStandardDuration()
    {
        // Arrange
        var queue = ReadMarkup("QueuePage.xaml");
        var compact = queue.Descendants(Xaml + "DataTemplate").Single(element => (string?)element.Attribute(X + "Key") == "CompactQueueItemTemplate");
        var list = queue.Descendants(Xaml + "ListView").Single(element => (string?)element.Attribute(X + "Name") == "CustomListView");

        // Act
        var duration = compact.Descendants(Xaml + "TextBlock").Single(element =>
            (string?)element.Attribute("Text") == "{x:Bind Duration, Converter={StaticResource DurationConverterShort}}");
        var source = compact.Descendants(Xaml + "TextBlock").Single(element => (string?)element.Attribute("Text") == "{x:Bind Source}");
        var standardDuration = list.Element(Xaml + "ListView.ItemTemplate")!.Descendants(Xaml + "TextBlock").Single(element =>
            (string?)element.Attribute("Text") == "{x:Bind Duration, Converter={StaticResource DurationConverter}}");

        // Assert
        Assert.AreEqual("4", (string?)duration.Attribute("Grid.Column"));
        Assert.AreEqual("5", (string?)source.Parent!.Parent!.Attribute("Grid.Column"));
        Assert.IsNotNull(standardDuration);
        Assert.IsTrue(queue.Descendants().Any(element => element.Name.LocalName == "DurationConverterShort"
            && (string?)element.Attribute(X + "Key") == "DurationConverterShort"));
    }

    [DataTestMethod]
    [DataRow("QueuePage.xaml")]
    [DataRow("Search.xaml")]
    [DataRow("LocalExplorerPage.xaml")]
    public void ExplicitBadges_UseSharedThemeBrushes(string pageName)
    {
        // Arrange
        var page = ReadMarkup(pageName);

        // Act
        var badges = page.Descendants(Xaml + "TextBlock").Where(element => (string?)element.Attribute("Text") == "E").ToArray();

        // Assert
        Assert.IsTrue(badges.Length > 0);
        foreach (var badge in badges)
        {
            Assert.AreEqual("{ThemeResource ExplicitBadgeForegroundBrush}", (string?)badge.Attribute("Foreground"));
            Assert.AreEqual("{ThemeResource ExplicitBadgeBackgroundBrush}", (string?)badge.Parent!.Attribute("Background"));
        }
    }

    [DataTestMethod]
    [DataRow("Light", "#A19FA4", "White")]
    [DataRow("Dark", "#A19FA4", "Black")]
    [DataRow("HighContrast", "{ThemeResource SystemColorWindowTextColor}", "{ThemeResource SystemColorWindowColor}")]
    public void ExplicitBadgeTheme_UsesInverseText_AndHonorsHighContrast(string theme, string background, string foreground)
    {
        // Arrange
        var app = ReadMarkup("App.xaml");
        var dictionary = app.Descendants(Xaml + "ResourceDictionary").Single(element => (string?)element.Attribute(X + "Key") == theme);

        // Act
        var backgroundBrush = dictionary.Elements(Xaml + "SolidColorBrush").Single(element => (string?)element.Attribute(X + "Key") == "ExplicitBadgeBackgroundBrush");
        var foregroundBrush = dictionary.Elements(Xaml + "SolidColorBrush").Single(element => (string?)element.Attribute(X + "Key") == "ExplicitBadgeForegroundBrush");

        // Assert
        Assert.AreEqual(background, (string?)backgroundBrush.Attribute("Color"));
        Assert.AreEqual(foreground, (string?)foregroundBrush.Attribute("Color"));
        Assert.IsNull(backgroundBrush.Attribute("Opacity"));
        Assert.IsNull(foregroundBrush.Attribute("Opacity"));
    }
}
