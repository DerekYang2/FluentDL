using FluentDL.Core.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class DurationParserTests
{
    [DataTestMethod]
    [DataRow("225", 0, 3, 45)]
    [DataRow("3:45", 0, 3, 45)]
    [DataRow("75:30", 1, 15, 30)]
    [DataRow("1:02:03", 1, 2, 3)]
    public void TryParse_ReadsSecondsAndClockText(string text, int hours, int minutes, int seconds)
    {
        // Act
        var parsed = DurationParser.TryParse(text, out var duration);

        // Assert
        Assert.IsTrue(parsed);
        Assert.AreEqual(new TimeSpan(hours, minutes, seconds), duration);
    }

    [TestMethod]
    public void TryParse_RoundsFractionalSeconds()
    {
        // Arrange
        var text = 180.6.ToString();

        // Act
        var parsed = DurationParser.TryParse(text, out var duration);

        // Assert
        Assert.IsTrue(parsed);
        Assert.AreEqual(TimeSpan.FromSeconds(181), duration);
    }

    [TestMethod]
    public void TryParse_AcceptsIntegerSeconds()
    {
        // Act
        var parsed = DurationParser.TryParse(225, out var duration);

        // Assert
        Assert.IsTrue(parsed);
        Assert.AreEqual(new TimeSpan(0, 3, 45), duration);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("abc")]
    [DataRow("3:4x")]
    [DataRow("3:-1")]
    [DataRow("1:2:3:4")]
    public void TryParse_RejectsUnreadableValues(string? text)
    {
        // Act
        var parsed = DurationParser.TryParse(text, out var duration);

        // Assert
        Assert.IsFalse(parsed);
        Assert.AreEqual(TimeSpan.Zero, duration);
    }
}
