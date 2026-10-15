using FluentDL.Core.Sources;
using FluentDL.Helpers;

namespace FluentDL.Characterization.Tests;

/// <summary>The services report StatusLevel and the pages show InfoBarSeverity. No network.</summary>
[TestClass]
public class StatusLevelTests
{
    [TestMethod]
    public void EveryLevelMapsToTheSeverityOfTheSameName()
    {
        foreach (var level in Enum.GetValues<StatusLevel>())
        {
            Assert.AreEqual(level.ToString(), level.ToSeverity().ToString());
        }
    }
}
