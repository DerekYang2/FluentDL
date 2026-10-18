using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class SpectrogramDialogMarkupTests
{
    private static XDocument Read()
    {
        using var stream = typeof(SpectrogramDialogMarkupTests).Assembly.GetManifestResourceStream("SpectrogramDialog.xaml");
        Assert.IsNotNull(stream);
        return XDocument.Load(stream);
    }

    [TestMethod]
    public void UsesIncrementalSpekControlInsteadOfAStaticImageOrLoadingRing()
    {
        var markup = Read();
        Assert.AreEqual(1, markup.Descendants(XName.Get("SpekView", "using:Spek.Net.WinUI")).Count());
        Assert.IsFalse(markup.Descendants().Any(element => element.Name.LocalName is "Image" or "ProgressRing" or "ScrollView"));
    }

    [TestMethod]
    public void UsesSoxPaletteForFluentDl()
    {
        var view = Read().Descendants(XName.Get("SpekView", "using:Spek.Net.WinUI")).Single();
        Assert.AreEqual("Sox", (string?)view.Attribute("Palette"));
    }

    [TestMethod]
    public void KeepsSaveInTheFooterAndResetWithInspectionControlsWithoutADefaultAction()
    {
        var dialog = Read().Descendants().Single(element => element.Name.LocalName == "ContentDialog");
        Assert.AreEqual("SaveImage_Click", (string?)dialog.Attribute("PrimaryButtonClick"));
        Assert.IsNull(dialog.Attribute("SecondaryButtonClick"));
        var reset = Read().Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SpectrogramResetZoom");
        Assert.AreEqual("ResetZoom_Click", (string?)reset.Attribute("Click"));
        Assert.AreEqual("Reset zoom", (string?)reset.Attribute("Content"));
        Assert.AreEqual("Dialog_Closing", (string?)dialog.Attribute("Closing"));
        Assert.AreEqual("False", (string?)dialog.Attribute("IsPrimaryButtonEnabled"));
        Assert.AreEqual("None", (string?)dialog.Attribute("DefaultButton"));
    }

    [TestMethod]
    public void InteractiveOptionsHaveAutomationIds()
    {
        var controls = Read().Descendants().Where(element => element.Name.LocalName is "AppBarButton" or "NumberBox" or "ComboBox" or "CheckBox" or "Button" or "Expander").ToArray();
        Assert.IsTrue(controls.Length > 0);
        foreach (var control in controls)
            Assert.IsNotNull(control.Attribute("AutomationProperties.AutomationId"), $"Missing AutomationId on {control.Name}.");
    }

    [TestMethod]
    public void FooterButtonsHaveDistinctIdsRatherThanSharedTemplateNames()
    {
        var ids = Read().Descendants().Where(element => element.Name.LocalName == "Setter"
                && (string?)element.Attribute("Property") == "AutomationProperties.AutomationId")
            .Select(element => (string?)element.Attribute("Value")).ToArray();
        CollectionAssert.AreEquivalent(new[] { "SpectrogramSave", "SpectrogramClose" }, ids);
    }

    [TestMethod]
    public void HeaderKeepsTheTitleLeftAndTheButtonsRight()
    {
        var markup = Read();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var header = markup.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "HeaderBar");
        Assert.AreEqual("Grid", header.Name.LocalName);
        var title = header.Elements().Single(element => (string?)element.Attribute("Text") == "Spectrogram");
        Assert.AreEqual("Left", (string?)title.Attribute("HorizontalAlignment"));
        var buttons = header.Elements().Single(element => (string?)element.Attribute(x + "Name") == "HeaderButtons");
        Assert.AreEqual("WrapPanel", buttons.Name.LocalName);
        Assert.AreEqual("Right", (string?)buttons.Attribute("HorizontalAlignment"));
        Assert.AreEqual("1", (string?)buttons.Attribute("Grid.Column"));
        Assert.AreEqual(6, buttons.Elements().Count(element => element.Name.LocalName == "Button"));
        Assert.IsFalse(markup.Descendants().Any(element => element.Name.LocalName is "CommandBar" or "AppBarButton"));
        Assert.IsNull(markup.Descendants().Single(element => element.Name.LocalName == "ContentDialog").Attribute("Title"));
    }

    [TestMethod]
    public void ButtonLabelsAreCenteredInsideTheirVisibleButtons()
    {
        var markup = Read();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = markup.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "SpectrogramButtonStyle");
        Assert.IsTrue(style.Elements().Any(element => (string?)element.Attribute("Property") == "HorizontalContentAlignment"
            && (string?)element.Attribute("Value") == "Center"));
        Assert.IsTrue(markup.Descendants().Where(element => element.Name.LocalName == "Button")
            .All(element => (string?)element.Attribute("Style") == "{StaticResource SpectrogramButtonStyle}"));
    }

    [TestMethod]
    public void ProfileCutoffAndReferenceToolsAreNotHiddenInFlyouts()
    {
        var markup = Read();
        foreach (var id in new[] { "SpectrogramReferences", "SpectrogramShowProfile", "SpectrogramShowCutoff" })
        {
            var control = markup.Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == id);
            Assert.IsFalse(control.Ancestors().Any(element => element.Name.LocalName == "Flyout"));
        }
        var references = markup.Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SpectrogramReferences");
        CollectionAssert.AreEqual(new[] { "None", "MP3 - LAME defaults" }, references.Elements().Select(element => (string?)element.Attribute("Content")).ToArray());
    }

    [TestMethod]
    public void ClearSelectionExplainsItsPurposeAndStartsHidden()
    {
        var button = Read().Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SpectrogramClearSelection");
        Assert.AreEqual("Clear selection", (string?)button.Attribute("Content"));
        Assert.AreEqual("False", (string?)button.Attribute("IsEnabled"));
        Assert.AreEqual("Collapsed", (string?)button.Attribute("Visibility"));
        StringAssert.Contains((string?)button.Attribute("ToolTipService.ToolTip"), "Zoom is unchanged");
    }

    [TestMethod]
    public void EscapeHandlingCoversTheBodyAndNativeCloseActionInsteadOfOnlyThePlot()
    {
        var markup = Read();
        var dialog = markup.Descendants().Single(element => element.Name.LocalName == "ContentDialog");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var body = markup.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "DialogBody");
        Assert.AreEqual("DialogBody_PreviewKeyDown", (string?)body.Attribute("PreviewKeyDown"));
        Assert.AreEqual("Dialog_CloseButtonClick", (string?)dialog.Attribute("CloseButtonClick"));
        Assert.IsNull(markup.Descendants(XName.Get("SpekView", "using:Spek.Net.WinUI")).Single().Attribute("PreviewKeyDown"));
    }

    [TestMethod]
    public void ReadoutUsesCompactFixedFieldsBesideLeftmostZoom()
    {
        var markup = Read();
        Assert.AreEqual(1, markup.Descendants().Count(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SpectrogramStatus"));
        Assert.IsFalse(markup.Descendants().Any(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SpectrogramReadout"));
        var zoom = markup.Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SpectrogramZoomReadout");
        Assert.AreEqual("Zoom: 100%", (string?)zoom.Attribute("Text"));
        Assert.IsNull(zoom.Attribute("Grid.Column"));
        var readout = markup.Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SpectrogramStatus");
        Assert.AreEqual("1", (string?)readout.Attribute("Grid.Column"));
        Assert.AreEqual("208", (string?)readout.Attribute("Width"));
        var fields = readout.Descendants().Where(element => element.Name.LocalName == "TextBlock").ToArray();
        Assert.AreEqual(3, fields.Length);
        Assert.IsTrue(fields.All(element => (string?)element.Attribute("TextAlignment") == "Right"));
        Assert.IsTrue(readout.Descendants().Where(element => element.Name.LocalName == "ColumnDefinition")
            .All(element => (string?)element.Attribute("Width") == "64"));
    }

    [TestMethod]
    public void MinimapUsesTheSameSpekSourceAndFitsAboveThePlot()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var map = Read().Descendants(XName.Get("SpectrogramOverview", "using:Spek.Net.WinUI")).Single();
        Assert.AreEqual("Overview", (string?)map.Attribute(x + "Name"));
        Assert.AreEqual("{x:Bind Spek, Mode=OneTime}", (string?)map.Attribute("Source"));
        Assert.AreEqual("Top", (string?)map.Attribute("VerticalAlignment"));
        Assert.AreEqual("1", (string?)map.Parent?.Attribute("Grid.Row"));
    }

    [TestMethod]
    public void DbInputsUseInlineSpinnersWithoutAnOverlappingPopup()
    {
        var inputs = Read().Descendants().Where(element => element.Name.LocalName == "NumberBox").ToArray();
        Assert.AreEqual(2, inputs.Length);
        foreach (var input in inputs)
        {
            Assert.AreEqual("Inline", (string?)input.Attribute("SpinButtonPlacementMode"));
            Assert.AreEqual("176", (string?)input.Attribute("Width"));
        }
    }

    [TestMethod]
    public void HintsUseSeparatedActionsWithoutAdvertisingPlusMinus()
    {
        var hints = Read().Descendants().Single(element => (string?)element.Attribute("AutomationProperties.AutomationId") == "SpectrogramHints");
        string text = string.Concat(hints.Elements().Select(element => (string?)element.Attribute("Text")));
        StringAssert.Contains(text, "pointer / Drag:");
        StringAssert.Contains(text, "cutoff / Esc:");
        StringAssert.Contains(text, "Clear spectrum profile selection");
        Assert.IsFalse(text.Contains("+/-"));
    }
}
