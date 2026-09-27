using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class QueueDisplayBindingTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument ReadMarkup(string name)
    {
        using var stream = typeof(QueueDisplayBindingTests).Assembly.GetManifestResourceStream(name);
        Assert.IsNotNull(stream);
        return XDocument.Load(stream);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void SpectrogramAction_RequiresLocalSource_AndYieldsToLiveProgress(bool useCompact)
    {
        // Arrange
        var page = ReadMarkup("QueuePage.xaml");
        var list = page.Descendants(Xaml + "ListView").Single(element => (string?)element.Attribute(X + "Name") == "CustomListView");
        var template = useCompact
            ? page.Descendants(Xaml + "DataTemplate").Single(element => (string?)element.Attribute(X + "Key") == "CompactQueueItemTemplate")
            : list.Element(Xaml + "ListView.ItemTemplate")!.Element(Xaml + "DataTemplate")!;

        // Act
        var spectrogram = template.Descendants().Single(element =>
            (string?)element.Attribute("Click") == "OpenSpekButton_Click"
            && !element.Ancestors(Xaml + "MenuFlyout").Any());
        var visibilityBindings = spectrogram.AncestorsAndSelf()
            .Select(element => (string?)element.Attribute("Visibility"))
            .OfType<string>()
            .ToArray();
        var localGuard = visibilityBindings.Single(binding => binding.Contains("LocalSourceToVisibilityConverter"));
        var idleGuard = visibilityBindings.Single(binding => binding.Contains("CollapsedConverter"));
        var progress = template.Descendants(Xaml + "ProgressRing").Single();

        // Assert
        StringAssert.Contains(localGuard, "Source");
        StringAssert.Contains(localGuard, "Mode=OneWay");
        StringAssert.Contains(idleGuard, "IsRunning");
        StringAssert.Contains(idleGuard, "Mode=OneWay");
        StringAssert.Contains((string)progress.Attribute("Visibility")!, "IsRunning");
        StringAssert.Contains((string)progress.Attribute("Visibility")!, "Mode=OneWay");
        StringAssert.Contains((string)progress.Attribute("Visibility")!, "StaticResource VisibilityConverter");
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CommandOutput_PassesTheQueueObject_AndTracksAvailableOutput(bool useCompact)
    {
        // Arrange
        var page = ReadMarkup("QueuePage.xaml");
        var list = page.Descendants(Xaml + "ListView").Single(element => (string?)element.Attribute(X + "Name") == "CustomListView");
        var template = useCompact
            ? page.Descendants(Xaml + "DataTemplate").Single(element => (string?)element.Attribute(X + "Key") == "CompactQueueItemTemplate")
            : list.Element(Xaml + "ListView.ItemTemplate")!.Element(Xaml + "DataTemplate")!;

        // Act
        var output = template.Descendants().Single(element =>
            (string?)element.Attribute("Click") == "OutputButton_OnClick");

        // Assert
        Assert.IsTrue((string?)output.Attribute("Tag") is "{x:Bind}" or "{Binding}",
            "The shared handler needs the row object, regardless of whether the action is a button or menu item.");
        StringAssert.Contains((string)output.Attribute("Visibility")!, "ResultString");
        StringAssert.Contains((string)output.Attribute("Visibility")!, "Mode=OneWay");
        StringAssert.Contains((string)output.Attribute("Visibility")!, "StaticResource PathToVisibilityConverter");
    }

    [TestMethod]
    public void BothToggles_ObserveTheSamePreference_AndDisableDuringUpdates()
    {
        // Arrange
        var queue = ReadMarkup("QueuePage.xaml");
        var settings = ReadMarkup("SettingsPage.xaml");

        // Act
        var queueToggle = queue.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "CompactQueueButton");
        var settingsToggle = settings.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "CompactQueueToggle");
        var preferenceBindings = new[] { queueToggle, settingsToggle }
            .Select(element => element.Attributes().Single(attribute =>
                attribute.Name.LocalName is "IsChecked" or "IsOn").Value)
            .ToArray();

        // Assert
        foreach (var binding in preferenceBindings)
        {
            StringAssert.Contains(binding, "ViewModel.QueueDisplay.IsCompact");
            StringAssert.Contains(binding, "Mode=OneWay");
        }
        foreach (var toggle in new[] { queueToggle, settingsToggle })
        {
            StringAssert.Contains((string)toggle.Attribute("IsEnabled")!, "ViewModel.QueueDisplay.CanChangeLayout");
            StringAssert.Contains((string)toggle.Attribute("IsEnabled")!, "Mode=OneWay");
        }
    }

    [DataTestMethod]
    [DataRow("QueuePage.xaml")]
    [DataRow("Search.xaml")]
    [DataRow("LocalExplorerPage.xaml")]
    public void ExplicitBadges_HideTheirBackgroundForCleanTracks(string pageName)
    {
        // Arrange
        var page = ReadMarkup(pageName);

        // Act
        var badges = page.Descendants().Where(element =>
            (string?)element.Attribute("Background") == "{ThemeResource ExplicitBadgeBackgroundBrush}").ToArray();

        // Assert
        Assert.IsTrue(badges.Length > 0);
        foreach (var badge in badges)
        {
            StringAssert.Contains((string)badge.Attribute("Visibility")!, "Explicit",
                "Hide the background as well as the letter; otherwise padding can leave an empty grey box.");
            StringAssert.Contains((string)badge.Attribute("Visibility")!, "Converter={StaticResource VisibilityConverter}");
        }
    }
}
