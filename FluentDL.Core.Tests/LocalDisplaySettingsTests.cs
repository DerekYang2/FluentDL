using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace FluentDL.Core.Tests;

[TestClass]
public class LocalDisplaySettingsTests
{
    [TestMethod]
    [DataRow(null, true)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public async Task LocalDefaultsToCompactAndPreservesSavedChoices(bool? saved, bool expected)
    {
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.ReadSettingAsync<bool?>(LocalDisplaySettings.SettingsKey)).ReturnsAsync(saved);
        var display = new LocalDisplaySettings(settings.Object);
        Assert.IsTrue(display.IsCompact);
        await display.LoadAsync();
        Assert.AreEqual(expected, display.IsCompact);
        settings.Verify(value => value.ReadSettingAsync<bool?>("local_compact_layout"), Times.Once);
        settings.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task QueueAndLocalSaveTheirLayoutsIndependently()
    {
        var values = new Dictionary<string, bool?>();
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.ReadSettingAsync<bool?>(It.IsAny<string>())).ReturnsAsync((string key) => values.GetValueOrDefault(key));
        settings.Setup(value => value.SaveSettingAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns((string key, bool value) => { values[key] = value; return Task.CompletedTask; });
        var local = new LocalDisplaySettings(settings.Object);
        var queue = new QueueDisplaySettings(settings.Object);
        await local.LoadAsync();
        await queue.LoadAsync();
        await local.SetCompactAsync(false);
        Assert.IsFalse(local.IsCompact);
        Assert.IsTrue(queue.IsCompact);
        var reopened = new LocalDisplaySettings(settings.Object);
        await reopened.LoadAsync();
        Assert.IsFalse(reopened.IsCompact);
        await queue.SetCompactAsync(false);
        await local.SetCompactAsync(true);
        Assert.IsFalse(queue.IsCompact);
        Assert.IsTrue(local.IsCompact);
    }

    [TestMethod]
    public async Task AFailedLocalSaveLeavesTheLayoutAndAllowsAnotherAttempt()
    {
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.SetupSequence(value => value.SaveSettingAsync(LocalDisplaySettings.SettingsKey, false))
            .ThrowsAsync(new IOException("Synthetic settings failure")).Returns(Task.CompletedTask);
        var display = new LocalDisplaySettings(settings.Object);
        await Assert.ThrowsExceptionAsync<IOException>(() => display.SetCompactAsync(false));
        Assert.IsTrue(display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        await display.SetCompactAsync(false);
        Assert.IsFalse(display.IsCompact);
    }
}
