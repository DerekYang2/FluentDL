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
    public void KeepsSaveAndResetInsideTheDialogWithoutADefaultAction()
    {
        var dialog = Read().Descendants().Single(element => element.Name.LocalName == "ContentDialog");
        Assert.AreEqual("SaveImage_Click", (string?)dialog.Attribute("PrimaryButtonClick"));
        Assert.AreEqual("ResetZoom_Click", (string?)dialog.Attribute("SecondaryButtonClick"));
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
        CollectionAssert.AreEquivalent(new[] { "SpectrogramSave", "SpectrogramResetZoom", "SpectrogramClose" }, ids);
    }

    [TestMethod]
    public void HeaderUsesLeftAlignedButtonsBesideTheTitle()
    {
        var markup = Read();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var header = markup.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "HeaderBar");
        Assert.AreEqual("WrapPanel", header.Name.LocalName);
        Assert.AreEqual("Left", (string?)header.Attribute("HorizontalAlignment"));
        Assert.IsTrue(header.Elements().Any(element => (string?)element.Attribute("Text") == "Spectrogram"));
        Assert.AreEqual(6, header.Elements().Count(element => element.Name.LocalName == "Button"));
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
}
