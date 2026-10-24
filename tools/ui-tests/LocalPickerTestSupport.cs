using FluentDL.Contracts.Services;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL;

// Only the explicit picker fixture build includes this toolbar. All picker dialogs are the native app dialogs.
internal static class LocalPickerTestSupport
{
    internal static void Attach(UIElement? content)
    {
        var shell = (ShellPage)content!;
        var frame = (Frame)shell.FindName("NavigationFrame");
        var root = (Grid)shell.Content;
        App.MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1000));
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 0, 0) };
        Canvas.SetZIndex(bar, 20);
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(status, "PickerFixtureStatus");
        LocalExplorerPage Page() => (LocalExplorerPage)frame.Content;
        void Add(string id, string text, RoutedEventHandler action)
        {
            var button = new Button { Content = text };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += action;
            bar.Children.Add(button);
        }
        async Task Run(Func<Task> action, string result)
        {
            try { await action(); status.Text = result; }
            catch (Exception error) { status.Text = $"ERROR {error.GetType().Name} 0x{error.HResult:X8}"; }
        }
        Add("PreparePickerFixture", "Prepare Local", (_, _) =>
        {
            App.GetService<INavigationService>().NavigateTo(typeof(LocalExplorerViewModel).FullName!);
            var path = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "picker-output-fixture");
            Directory.CreateDirectory(path);
            ((TextBox)Page().FindName("OutputTextBox")).Text = path;
            status.Text = "ready";
        });
        Add("WorkerNoticeFixture", "Worker notice", async (_, _) => await Run(Page().ShowWorkerNoticeFixtureAsync, "worker OK"));
        Add("EmptyFilesFixture", "Empty files", async (_, _) => await Run(Page().ProcessEmptyFilesFixtureAsync, "empty OK"));
        Add("NoticeFadeFixture", "Notice replacement", async (_, _) => await Run(Page().ReplaceFadingNoticeFixtureAsync, "replacement OK"));
        Add("PickerSettingsFixture", "Settings pickers", (_, _) =>
        {
            App.GetService<INavigationService>().NavigateTo(typeof(SettingsViewModel).FullName!);
            ((FrameworkElement)((SettingsPage)frame.Content).FindName("ImportExportSettingsCard")).StartBringIntoView();
        });
        Add("ClosePickerFixture", "Close fixture", (_, _) => App.MainWindow.Close());
        bar.Children.Add(status);
        root.Children.Add(bar);
    }
}
