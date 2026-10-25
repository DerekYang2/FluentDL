using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class QueueArtworkMarkupTests
{
    [TestMethod]
    public void BothQueueLayoutsUseTheSameArtworkControlWithRecyclingBindings()
    {
        foreach (var name in new[] { "CompactQueueRow.xaml", "QueuePage.xaml" })
        {
            var tile = Read(name).Descendants().Single(element => element.Name.LocalName == "QueueArtworkTile");
            StringAssert.Contains((string)tile.Attribute("Item")!, "Mode=OneWay");
            Assert.AreEqual(name == "CompactQueueRow.xaml" ? "44" : "76", (string?)tile.Attribute("Width"));
            Assert.AreEqual((string?)tile.Attribute("Width"), (string?)tile.Attribute("Height"));
            Assert.AreNotEqual("True", (string?)tile.Attribute("UseUrlFallback"));
        }
    }

    [TestMethod]
    public void SkeletonUsesThemeBrushesAndLifecycleHooksWithoutADirectUriInMarkup()
    {
        var control = Read("QueueArtworkTile.xaml").Root!;
        Assert.AreEqual("False", (string?)control.Attribute("IsTabStop"));
        Assert.IsNotNull(control.Attribute("Loaded"));
        Assert.IsNotNull(control.Attribute("Unloaded"));
        Assert.IsNotNull(control.Attribute("EffectiveViewportChanged"));
        var skeleton = control.Descendants().Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "Skeleton"));
        Assert.AreEqual("4", (string?)skeleton.Attribute("CornerRadius"));
        StringAssert.StartsWith((string)skeleton.Attribute("Background")!, "{ThemeResource ");
        Assert.IsFalse(control.Descendants().Any(element => element.Name.LocalName == "BitmapImage"));
        Assert.IsFalse(control.ToString().Contains("ImageLocation"));
        Assert.IsFalse(control.ToString().Contains("UriSource"));
    }

    [TestMethod]
    public void SearchExplicitlyOptsIntoUrlArtworkWhileQueueSummaryRemainsCachedOnly()
    {
        var search = Read("Search.xaml").Descendants().Single(element => element.Name.LocalName == "QueueArtworkTile");
        Assert.AreEqual("True", (string?)search.Attribute("UseUrlFallback"));
        StringAssert.Contains((string)search.Attribute("Item")!, "Mode=OneWay");
        var result = Read("QueuePage.xaml").Descendants().Single(element => element.Name.LocalName == "CompactResultRow");
        Assert.AreEqual("True", (string?)result.Attribute("CachedArtworkOnly"));
    }

    private static XDocument Read(string name)
    {
        using var stream = typeof(QueueArtworkMarkupTests).Assembly.GetManifestResourceStream(name)!;
        return XDocument.Load(stream);
    }
}
