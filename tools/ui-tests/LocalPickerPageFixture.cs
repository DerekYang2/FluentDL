using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FluentDL.Views;

// Compiled only by PickerCancellationPreview.targets. Exercises the real notification paths from a worker.
public sealed partial class LocalExplorerPage
{
    internal Task ShowWorkerNoticeFixtureAsync() => Task.Run(() =>
        ShowInfoBar(InfoBarSeverity.Informational, "Background notice delivered.", 10));

    internal Task ProcessEmptyFilesFixtureAsync() => Task.Run(() => ProcessFiles(Array.Empty<StorageFile>()));

    internal async Task ReplaceFadingNoticeFixtureAsync()
    {
        var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Ticked(object? sender, object args) => tick.TrySetResult();
        dispatcherTimer.Tick += Ticked;
        try
        {
            ShowInfoBar(InfoBarSeverity.Informational, "Old timed notice.", 1);
            await tick.Task.WaitAsync(TimeSpan.FromSeconds(5));
            ShowInfoBarPermanent(InfoBarSeverity.Informational, "New permanent progress notice.");
            await Task.Delay(400);
            if (!PageInfoBar.IsOpen || PageInfoBar.Opacity != 1)
                throw new InvalidOperationException("An old fade hid the replacement notice.");
        }
        finally { dispatcherTimer.Tick -= Ticked; }
    }
}
