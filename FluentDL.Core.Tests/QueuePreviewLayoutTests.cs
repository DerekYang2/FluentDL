using FluentDL.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class QueuePreviewLayoutTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(320)]
    [DataRow(600)]
    [DataRow(800)]
    [DataRow(839)]
    public void NarrowPageStacksThePreviewWithoutReservingASeparateColumn(double width)
    {
        var layout = QueuePreviewLayout.ForWidth(width);
        Assert.IsTrue(layout.IsStacked);
        Assert.AreEqual(width, layout.AvailablePreviewWidth);
    }

    [TestMethod]
    [DataRow(840)]
    [DataRow(1000)]
    [DataRow(1600)]
    public void WidePageReservesAReadableQueueWidthAndAGap(double width)
    {
        var layout = QueuePreviewLayout.ForWidth(width);
        Assert.IsFalse(layout.IsStacked);
        Assert.AreEqual(412, width - layout.AvailablePreviewWidth);
    }

    [TestMethod]
    public void InvalidMeasurementsCannotProduceNegativeOrNonfinitePreviewWidths()
    {
        foreach (var width in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            var layout = QueuePreviewLayout.ForWidth(width);
            Assert.IsTrue(layout.IsStacked);
            Assert.AreEqual(0, layout.AvailablePreviewWidth);
        }
    }
}
