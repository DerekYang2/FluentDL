using FluentDL.Contracts.Services;
using FluentDL.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace FluentDL.Core.Tests;

[TestClass]
public class QueueDisplaySettingsTests
{
    [TestMethod]
    public void Constructor_UsesStandardLayout_AndRejectsMissingSettings()
    {
        // Arrange
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);

        // Act
        var display = new QueueDisplaySettings(settings.Object);
        var error = Assert.ThrowsException<ArgumentNullException>(() => new QueueDisplaySettings(null!));

        // Assert
        Assert.IsFalse(display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        Assert.AreEqual("settings", error.ParamName);
        settings.VerifyNoOtherCalls();
    }

    [DataTestMethod]
    [DataRow(null, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public async Task LoadAsync_ReadsPreference_AndInitializesOnlyMissingDefaults(bool? saved, bool expected)
    {
        // Arrange
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey)).ReturnsAsync(saved);
        if (saved is null)
        {
            settings.Setup(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false)).Returns(Task.CompletedTask);
        }
        var display = new QueueDisplaySettings(settings.Object);
        var changes = new List<string?>();
        display.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        // Act
        await display.LoadAsync();

        // Assert
        Assert.AreEqual(expected, display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        Assert.AreEqual(expected ? 1 : 0, changes.Count(name => name == nameof(display.IsCompact)));
        Assert.AreEqual(2, changes.Count(name => name == nameof(display.CanChangeLayout)));
        settings.Verify(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey), Times.Once);
        settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false), saved is null ? Times.Once() : Times.Never());
        settings.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task LoadAsync_ReloadsImportedOrResetPreference()
    {
        // Arrange
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.SetupSequence(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey))
            .ReturnsAsync(true).ReturnsAsync(false).ReturnsAsync((bool?)null);
        settings.Setup(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false)).Returns(Task.CompletedTask);
        var display = new QueueDisplaySettings(settings.Object);
        await display.LoadAsync();

        // Act
        await display.LoadAsync();
        var afterReset = display.IsCompact;
        await display.LoadAsync();

        // Assert
        Assert.IsFalse(afterReset);
        Assert.IsFalse(display.IsCompact);
        settings.Verify(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey), Times.Exactly(3));
        settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false), Times.Once);
        settings.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task LoadAsync_OnFailureRetainsPreviousLayout_AndAllowsRetry()
    {
        // Arrange
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.SetupSequence(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey))
            .ReturnsAsync(true).ThrowsAsync(new IOException("Read failed")).ReturnsAsync(false);
        var display = new QueueDisplaySettings(settings.Object);
        await display.LoadAsync();

        // Act
        await Assert.ThrowsExceptionAsync<IOException>(() => display.LoadAsync());
        var retained = display.IsCompact;
        var canRetry = display.CanChangeLayout;
        await display.LoadAsync();

        // Assert
        Assert.IsTrue(retained);
        Assert.IsTrue(canRetry);
        Assert.IsFalse(display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        settings.Verify(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey), Times.Exactly(3));
        settings.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task SetCompactAsync_NotifiesBothConsumersOnlyAfterSaving_AndPersistsAcrossInstances()
    {
        // Arrange
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? saved = null;
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, true)).Returns(async () =>
        {
            await save.Task;
            saved = true;
        });
        settings.Setup(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey)).ReturnsAsync(() => saved);
        var display = new QueueDisplaySettings(settings.Object);
        var queueChanges = 0;
        var settingsChanges = 0;
        display.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(display.IsCompact)) queueChanges++; };
        display.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(display.IsCompact)) settingsChanges++; };

        // Act
        var update = display.SetCompactAsync(true);
        var beforeSaving = display.IsCompact;
        var enabledDuringSave = display.CanChangeLayout;
        save.SetResult();
        await update;
        var reopened = new QueueDisplaySettings(settings.Object);
        await reopened.LoadAsync();

        // Assert
        Assert.IsFalse(beforeSaving);
        Assert.IsFalse(enabledDuringSave);
        Assert.IsTrue(display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        Assert.AreEqual(1, queueChanges);
        Assert.AreEqual(1, settingsChanges);
        Assert.IsTrue(reopened.IsCompact);
        settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, true), Times.Once);
        settings.Verify(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey), Times.Once);
        settings.VerifyNoOtherCalls();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SetCompactAsync_OnFailureRetainsPreviousLayout_AndAllowsRetry(bool initial)
    {
        // Arrange
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey)).ReturnsAsync(initial);
        settings.SetupSequence(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, !initial))
            .ThrowsAsync(new IOException("Save failed")).Returns(Task.CompletedTask);
        var display = new QueueDisplaySettings(settings.Object);
        await display.LoadAsync();
        var changes = 0;
        display.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(display.IsCompact)) changes++; };

        // Act
        await Assert.ThrowsExceptionAsync<IOException>(() => display.SetCompactAsync(!initial));
        var retained = display.IsCompact;
        var changesAfterFailure = changes;
        var canRetry = display.CanChangeLayout;
        await display.SetCompactAsync(!initial);

        // Assert
        Assert.AreEqual(initial, retained);
        Assert.AreEqual(0, changesAfterFailure);
        Assert.IsTrue(canRetry);
        Assert.AreEqual(!initial, display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        Assert.AreEqual(1, changes);
        settings.Verify(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey), Times.Once);
        settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, !initial), Times.Exactly(2));
        settings.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task LoadAsync_SurfacesDefaultWriteFailures()
    {
        // Arrange
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey)).ReturnsAsync((bool?)null);
        settings.Setup(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false)).ThrowsAsync(new IOException("Save failed"));
        var display = new QueueDisplaySettings(settings.Object);

        // Act
        await Assert.ThrowsExceptionAsync<IOException>(() => display.LoadAsync());

        // Assert
        Assert.IsFalse(display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        settings.Verify(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey), Times.Once);
        settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false), Times.Once);
        settings.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task SetCompactAsync_PersistsUnchangedValuesWithoutLayoutNotifications()
    {
        // Arrange
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        settings.Setup(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false)).Returns(Task.CompletedTask);
        var display = new QueueDisplaySettings(settings.Object);
        var changes = 0;
        display.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(display.IsCompact)) changes++; };

        // Act
        await display.SetCompactAsync(false);

        // Assert
        Assert.IsFalse(display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        Assert.AreEqual(0, changes);
        settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false), Times.Once);
        settings.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task SetCompactAsync_SerializesCompetingTogglesAndReads()
    {
        // Arrange
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        var order = new MockSequence();
        settings.InSequence(order).Setup(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, true)).Returns(save.Task);
        settings.InSequence(order).Setup(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false)).Returns(Task.CompletedTask);
        settings.InSequence(order).Setup(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey)).ReturnsAsync(false);
        var display = new QueueDisplaySettings(settings.Object);

        // Act
        var first = display.SetCompactAsync(true);
        var second = display.SetCompactAsync(false);
        var reload = display.LoadAsync();
        var secondWasWaiting = !second.IsCompleted;
        var reloadWasWaiting = !reload.IsCompleted;
        save.SetResult();
        await Task.WhenAll(first, second, reload);

        // Assert
        Assert.IsTrue(secondWasWaiting);
        Assert.IsTrue(reloadWasWaiting);
        Assert.IsFalse(display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, true), Times.Once);
        settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false), Times.Once);
        settings.Verify(value => value.ReadSettingAsync<bool?>(QueueDisplaySettings.SettingsKey), Times.Once);
        settings.VerifyNoOtherCalls();
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SetCompactAsync_FailedSaveDoesNotBlockTheNextQueuedChange(bool next)
    {
        // Arrange
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new Mock<ILocalSettingsService>(MockBehavior.Strict);
        var order = new MockSequence();
        settings.InSequence(order).Setup(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, true)).Returns(save.Task);
        settings.InSequence(order).Setup(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, next)).Returns(Task.CompletedTask);
        var display = new QueueDisplaySettings(settings.Object);
        var publishedStates = new List<bool>();
        display.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(display.IsCompact)) publishedStates.Add(display.IsCompact);
        };

        // Act
        var first = display.SetCompactAsync(true);
        var queued = display.SetCompactAsync(next);
        var wasWaiting = !queued.IsCompleted;
        save.SetException(new IOException("Save failed"));
        await Assert.ThrowsExceptionAsync<IOException>(() => first);
        await queued.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.IsTrue(wasWaiting);
        Assert.AreEqual(next, display.IsCompact);
        Assert.IsTrue(display.CanChangeLayout);
        CollectionAssert.AreEqual(next ? new[] { true } : Array.Empty<bool>(), publishedStates.ToArray(),
            "Only successfully persisted changes should be published.");
        settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, true),
            Times.Exactly(next ? 2 : 1));
        if (!next)
        {
            settings.Verify(value => value.SaveSettingAsync(QueueDisplaySettings.SettingsKey, false), Times.Once);
        }
        settings.VerifyNoOtherCalls();
    }
}
