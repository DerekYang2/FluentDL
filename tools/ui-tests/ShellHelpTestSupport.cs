using System.Runtime.InteropServices;
using FluentDL.Contracts.Services;
using FluentDL.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL;

internal static class ShellHelpTestSupport
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    internal static void Attach(UIElement? content)
    {
        var shell = (ShellPage)content!;
        var root = (Grid)shell.Content;
        var frame = (Frame)shell.FindName("NavigationFrame");
        var navigation = (NavigationView)shell.FindName("NavigationViewControl");
        var originalOpener = shell.OpenHelpAsync;
        var mode = "record";
        var calls = 0;
        var launched = false;
        var lastUri = "";
        object? page = null;
        object? selected = null;
        root.RowDefinitions.Add(new() { Height = new GridLength(48) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        foreach (var child in root.Children) Grid.SetRow((FrameworkElement)child, ((FrameworkElement)child).Name == "AppTitleBar" ? 0 : 2);
        var panel = new StackPanel { Margin = new Thickness(16, 4, 16, 8), Spacing = 4 };
        Grid.SetRow(panel, 1);
        root.Children.Add(panel);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(buttons);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, "HelpFixtureStatus");
        panel.Children.Add(status);
        void Read() => status.Text = $"mode={mode}; calls={calls}; launched={launched}; samePage={ReferenceEquals(page, frame.Content)}; sameSelection={ReferenceEquals(selected, navigation.SelectedItem)}; uri={lastUri}";
        void Add(string id, string text, RoutedEventHandler handler)
        {
            var button = new Button { Content = text };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += handler;
            buttons.Children.Add(button);
        }
        Add("HelpFixtureArm", "Record Help", (_, _) => { mode = "record"; page = frame.Content; selected = navigation.SelectedItem; Read(); });
        Add("HelpFixtureFail", "Reject launch", (_, _) => { mode = "fail"; Read(); });
        Add("HelpFixtureThrow", "Throw on launch", (_, _) => { mode = "throw"; Read(); });
        Add("HelpFixtureReal", "Real browser", (_, _) => { mode = "real"; Read(); });
        Add("HelpFixtureCompact", "Toggle pane", (_, _) => navigation.IsPaneOpen = !navigation.IsPaneOpen);
        Add("HelpFixtureDark", "Dark", async (_, _) => await App.GetService<IThemeSelectorService>().SetThemeAsync(ElementTheme.Dark));
        Add("HelpFixtureLight", "Light", async (_, _) => await App.GetService<IThemeSelectorService>().SetThemeAsync(ElementTheme.Light));
        Add("HelpFixtureClose", "Close", (_, _) => PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow), 0x0010, IntPtr.Zero, IntPtr.Zero));
        shell.OpenHelpAsync = async uri =>
        {
            calls++;
            lastUri = uri.AbsoluteUri;
            launched = false;
            Read();
            if (mode == "throw") throw new InvalidOperationException("Synthetic browser launch failure.");
            launched = mode == "real" ? await originalOpener(uri) : mode != "fail";
            Read();
            return launched;
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => Read();
        timer.Start();
        App.MainWindow.Closed += (_, _) => timer.Stop();
        App.MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 950));
        navigation.IsPaneOpen = true;
        Read();
    }
}
