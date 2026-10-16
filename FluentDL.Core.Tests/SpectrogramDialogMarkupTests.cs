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
}
