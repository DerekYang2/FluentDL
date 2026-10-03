using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class QueueDisplayBindingTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Views = "using:FluentDL.Views";

    private sealed record XBind(string Path, string? Mode, string? Converter);

    private static XDocument ReadMarkup(string name)
    {
        using var stream = typeof(QueueDisplayBindingTests).Assembly.GetManifestResourceStream(name);
        Assert.IsNotNull(stream);
        return XDocument.Load(stream);
    }

    private static XBind ParseXBind(string? markup)
    {
        Assert.IsNotNull(markup);
        StringAssert.StartsWith(markup, "{x:Bind");
        var body = markup["{x:Bind".Length..^1].Trim();
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '{') depth++;
            else if (body[i] == '}') depth--;
            else if (body[i] == ',' && depth == 0)
            {
                parts.Add(body[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(body[start..].Trim());

        string? Named(string name) => parts.FirstOrDefault(part => part.StartsWith(name + "="))?[(name.Length + 1)..];
        return new XBind(parts.FirstOrDefault(part => !part.Contains('=')) ?? "", Named("Mode"), Named("Converter"));
    }

    private static XElement StandardTemplate(XDocument page) =>
        page.Descendants(Xaml + "ListView")
            .Single(element => (string?)element.Attribute(X + "Name") == "CustomListView")
            .Element(Xaml + "ListView.ItemTemplate")!
            .Element(Xaml + "DataTemplate")!;

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SpectrogramAction_RequiresLocalSource_AndYieldsToLiveProgress(bool useCompact)
    {
        // Arrange
        var row = useCompact ? ReadMarkup("CompactQueueRow.xaml").Root! : StandardTemplate(ReadMarkup("QueuePage.xaml"));
        var prefix = useCompact ? "Item." : "";

        // Act
        var spectrogram = row.Descendants().Single(element =>
            (string?)element.Attribute("Click") is "OpenSpekButton_Click" or "SpectrogramButton_Click");
        var guards = spectrogram.AncestorsAndSelf()
            .Select(element => (string?)element.Attribute("Visibility"))
            .OfType<string>()
            .Select(ParseXBind)
            .ToArray();
        var localGuard = guards.Single(binding => binding.Converter == "{StaticResource LocalSourceToVisibilityConverter}");
        var idleGuard = guards.Single(binding => binding.Converter == "{StaticResource CollapsedConverter}");
        var progress = ParseXBind((string?)row.Descendants(Xaml + "ProgressRing").Single().Attribute("Visibility"));

        // Assert
        Assert.AreEqual(prefix + "Source", localGuard.Path);
        Assert.AreEqual(prefix + "IsRunning", idleGuard.Path);
        Assert.AreEqual("OneWay", idleGuard.Mode);
        Assert.AreEqual(prefix + "IsRunning", progress.Path);
        Assert.AreEqual("OneWay", progress.Mode);
        Assert.AreEqual("{StaticResource VisibilityConverter}", progress.Converter);
    }

    [TestMethod]
    public void CompactRow_UpdatesEveryItemBinding_WhenTheRowIsRecycled()
    {
        // Arrange
        var row = ReadMarkup("CompactQueueRow.xaml");

        // Act
        var itemBindings = row.Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Value.StartsWith("{x:Bind"))
            .Select(attribute => (attribute, binding: ParseXBind(attribute.Value)))
            .Where(pair => pair.binding.Path == "Item" || pair.binding.Path.StartsWith("Item."))
            .ToArray();

        // Assert
        Assert.IsTrue(itemBindings.Length > 0);
        foreach (var (attribute, binding) in itemBindings)
        {
            Assert.AreEqual("OneWay", binding.Mode, $"{attribute.Name} = {attribute.Value} would keep the previous track after recycling.");
        }
    }

    [TestMethod]
    public void StandardRows_PassTheQueueObjectToCommandOutput_AndTrackAvailableOutput()
    {
        // Arrange
        var template = StandardTemplate(ReadMarkup("QueuePage.xaml"));

        // Act
        var output = template.Descendants().Single(element => (string?)element.Attribute("Click") == "OutputButton_OnClick");
        var visibility = ParseXBind((string?)output.Attribute("Visibility"));

        // Assert
        Assert.AreEqual("{Binding}", (string?)output.Attribute("Tag"));
        Assert.AreEqual("ResultString", visibility.Path);
        Assert.AreEqual("OneWay", visibility.Mode);
        Assert.AreEqual("{StaticResource PathToVisibilityConverter}", visibility.Converter);
    }

    [TestMethod]
    public void CompactRows_ShareOneContextMenu_WithEveryRowAction()
    {
        // Arrange
        var page = ReadMarkup("QueuePage.xaml");
        var row = ReadMarkup("CompactQueueRow.xaml");

        // Act
        var menu = page.Descendants(Xaml + "MenuFlyout").Single(element => (string?)element.Attribute(X + "Name") == "CompactQueueRowMenu");
        var handlers = menu.Elements(Xaml + "MenuFlyoutItem").Select(item => (string?)item.Attribute("Click")).ToArray();

        // Assert
        CollectionAssert.AreEquivalent(
            new[] { "ShareLinkButton_OnClick", "DownloadCoverButton_OnClick", "RemoveButton_OnClick", "OpenSpekButton_Click", "OpenLocalButton_Click", "OutputButton_OnClick" },
            handlers);
        Assert.IsFalse(row.Descendants(Xaml + "MenuFlyout").Any(), "Rows use the shared menu so the requested row, not the selection, is the target.");
    }

    [TestMethod]
    public void ListToggle_BindsToThePreference_AndDisablesDuringSaves()
    {
        // Arrange
        var queue = ReadMarkup("QueuePage.xaml");

        // Act
        var toggle = queue.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "CompactQueueButton");
        var preference = ParseXBind((string?)toggle.Attribute("IsChecked"));
        var enabled = ParseXBind((string?)toggle.Attribute("IsEnabled"));

        // Assert
        Assert.AreEqual("ViewModel.QueueDisplay.IsCompact", preference.Path);
        Assert.AreEqual("OneWay", preference.Mode);
        Assert.AreEqual("ViewModel.QueueDisplay.CanChangeLayout", enabled.Path);
        Assert.AreEqual("OneWay", enabled.Mode);
    }

    [DataTestMethod]
    [DataRow("QueuePage.xaml")]
    [DataRow("CompactQueueRow.xaml")]
    [DataRow("Search.xaml")]
    [DataRow("LocalExplorerPage.xaml")]
    public void ExplicitBadges_UseTheSharedBadge_AndHideAsAWhole(string pageName)
    {
        // Arrange
        var page = ReadMarkup(pageName);

        // Act
        var badges = page.Descendants(Views + "ExplicitBadge").ToArray();
        var inlineBadges = page.Descendants().Where(element =>
            element.Attributes().Any(attribute => attribute.Value.Contains("ExplicitBadgeBackgroundBrush")));

        // Assert
        Assert.IsTrue(badges.Length > 0);
        Assert.IsFalse(inlineBadges.Any(), "Use views:ExplicitBadge so every page shares one badge definition.");
        foreach (var badge in badges)
        {
            var visibility = ParseXBind((string?)badge.Attribute("Visibility"));
            Assert.IsTrue(visibility.Path == "Explicit" || visibility.Path.EndsWith(".Explicit"), visibility.Path);
            Assert.AreEqual("{StaticResource VisibilityConverter}", visibility.Converter);
        }
    }

    [TestMethod]
    public void ExplicitBadge_DoesNotHideItsOwnParts()
    {
        // Arrange
        var badge = ReadMarkup("ExplicitBadge.xaml");

        // Act
        var partsWithVisibility = badge.Descendants().Where(element => element.Attribute("Visibility") is not null);

        // Assert
        Assert.IsFalse(partsWithVisibility.Any(), "Pages hide the whole badge; hiding only the letter leaves an empty grey box.");
    }
}
