using System.Globalization;
using FluentDL.Core.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class DisplayFormatTests
{
    // Pins the output YouTube view counts and Spotify play counts showed before the helper moved to Core.
    [DataTestMethod]
    [DataRow(0L, "0")]
    [DataRow(999L, "999")]
    [DataRow(1_000L, "1K")]
    [DataRow(1_499L, "1K")]
    [DataRow(1_500L, "2K")]
    [DataRow(999_499L, "999K")]
    [DataRow(1_000_000L, "1.0M")]
    [DataRow(1_234_567L, "1.2M")]
    [DataRow(999_999_999L, "1000.0M")]
    [DataRow(1_000_000_000L, "1.00B")]
    [DataRow(2_546_000_000L, "2.55B")]
    public void FormatLargeValue_KeepsThePreviousOutput(long value, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.AreEqual(expected, DisplayFormat.FormatLargeValue(value));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
