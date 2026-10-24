using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace FluentDL.Core.Tests;

[TestClass]
public class PreviewVolumeSettingsTests
{
    [TestMethod]
    public void NewSessionUsesFullVolumeAndRejectsMissingSettings()
    {
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        Assert.AreEqual(1d, new PreviewVolumeSettings(settings.Object).Volume);
        Assert.ThrowsException<ArgumentNullException>(() => new PreviewVolumeSettings(null!));
        settings.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(null, 1d)]
    [DataRow(0d, 0d)]
    [DataRow(0.27, 0.27)]
    [DataRow(1d, 1d)]
    [DataRow(-0.5, 0d)]
    [DataRow(2d, 1d)]
    [DataRow(double.NaN, 1d)]
    [DataRow(double.PositiveInfinity, 1d)]
    public async Task LoadsTheExistingSettingAndNormalizesInvalidSavedValues(double? saved, double expected)
    {
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.ReadSettingAsync<double?>(PreviewVolumeSettings.SettingsKey)).ReturnsAsync(saved);
        var volume = new PreviewVolumeSettings(settings.Object);
        await volume.LoadAsync();
        Assert.AreEqual(expected, volume.Volume);
        settings.Verify(value => value.ReadSettingAsync<double?>("preview_player_volume"), Times.Once);
        settings.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task SharesChangesBeforeSavingAndRestoresThemInANewSession()
    {
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        double? saved = null;
        settings.Setup(value => value.SaveSettingAsync(PreviewVolumeSettings.SettingsKey, 0.27)).Returns(async () =>
        {
            await release.Task;
            saved = 0.27;
        });
        settings.Setup(value => value.ReadSettingAsync<double?>(PreviewVolumeSettings.SettingsKey)).ReturnsAsync(() => saved);
        var volume = new PreviewVolumeSettings(settings.Object);
        var searchPage = volume;
        var queuePage = volume;
        var explorerPage = volume;

        var save = searchPage.SetVolumeAsync(0.27);
        Assert.IsFalse(save.IsCompleted);
        Assert.AreEqual(0.27, queuePage.Volume);
        Assert.AreEqual(0.27, explorerPage.Volume);
        release.SetResult();
        await save;
        var restarted = new PreviewVolumeSettings(settings.Object);
        await restarted.LoadAsync();
        Assert.AreEqual(0.27, restarted.Volume);
    }

    [TestMethod]
    public async Task OverlappingSliderChangesSerializeSavesAndKeepTheLatestValue()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<double>();
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.SaveSettingAsync(PreviewVolumeSettings.SettingsKey, It.IsAny<double>()))
            .Returns(async (string _, double value) =>
            {
                writes.Add(value);
                if (writes.Count == 1) await release.Task;
            });
        var volume = new PreviewVolumeSettings(settings.Object);
        var first = volume.SetVolumeAsync(0.8);
        var second = volume.SetVolumeAsync(0.4);
        var last = volume.SetVolumeAsync(0);
        Assert.AreEqual(0d, volume.Volume);
        CollectionAssert.AreEqual(new[] { 0.8 }, writes);
        release.SetResult();
        await Task.WhenAll(first, second, last);
        Assert.AreEqual(0d, writes.Last());
        Assert.AreEqual(0d, volume.Volume);
    }

    [TestMethod]
    public async Task StartupReadCannotOverwriteANewerUserChange()
    {
        var read = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.ReadSettingAsync<double?>(PreviewVolumeSettings.SettingsKey)).Returns(read.Task);
        settings.Setup(value => value.SaveSettingAsync(PreviewVolumeSettings.SettingsKey, 0.15)).Returns(Task.CompletedTask);
        var volume = new PreviewVolumeSettings(settings.Object);
        var load = volume.LoadAsync();
        var save = volume.SetVolumeAsync(0.15);
        read.SetResult(0.9);
        await Task.WhenAll(load, save);
        Assert.AreEqual(0.15, volume.Volume);
        settings.Verify(value => value.SaveSettingAsync(PreviewVolumeSettings.SettingsKey, 0.15), Times.Once);
    }

    [TestMethod]
    public async Task ReloadAppliesImportedOrResetSettings()
    {
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.SetupSequence(value => value.ReadSettingAsync<double?>(PreviewVolumeSettings.SettingsKey))
            .ReturnsAsync(0.3).ReturnsAsync(0.7).ReturnsAsync((double?)null);
        var volume = new PreviewVolumeSettings(settings.Object);
        await volume.LoadAsync();
        Assert.AreEqual(0.3, volume.Volume);
        await volume.LoadAsync();
        Assert.AreEqual(0.7, volume.Volume);
        await volume.LoadAsync();
        Assert.AreEqual(1d, volume.Volume);
    }

    [TestMethod]
    public async Task FailedSaveDoesNotLoseTheSessionVolumeOrBlockLaterSaves()
    {
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.SetupSequence(value => value.SaveSettingAsync(PreviewVolumeSettings.SettingsKey, 0.2))
            .ThrowsAsync(new IOException("Synthetic settings failure")).Returns(Task.CompletedTask);
        var volume = new PreviewVolumeSettings(settings.Object);
        await Assert.ThrowsExceptionAsync<IOException>(() => volume.SetVolumeAsync(0.2));
        Assert.AreEqual(0.2, volume.Volume);
        await volume.SetVolumeAsync(0.2);
        settings.Verify(value => value.SaveSettingAsync(PreviewVolumeSettings.SettingsKey, 0.2), Times.Exactly(2));
    }

    [TestMethod]
    [DataRow(-0.1)]
    [DataRow(1.1)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    public async Task InvalidPlayerValuesCannotBePublishedOrSaved(double value)
    {
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        var volume = new PreviewVolumeSettings(settings.Object);
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => volume.SetVolumeAsync(value));
        Assert.AreEqual(1d, volume.Volume);
        settings.VerifyNoOtherCalls();
    }
}
