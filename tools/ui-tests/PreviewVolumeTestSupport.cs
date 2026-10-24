using FluentDL.Core.Contracts.Services;
using FluentDL.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;

namespace FluentDL;

// Only compiled by PreviewVolumePreview.targets. The real pages and players use a generated silent WAV.
internal static class PreviewVolumeTestSupport
{
    internal static void Attach(UIElement? content)
    {
        if (content is not ShellPage shell || shell.Content is not Grid root) throw new InvalidOperationException("No test shell.");
        var frame = (Frame)shell.FindName("NavigationFrame");
        App.MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 900));
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 40, 0, 0),
            Spacing = 8,
        };
        Canvas.SetZIndex(bar, 20);
        var readback = new TextBlock { Text = "not loaded", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(readback, "PreviewVolumeReadback");
        var saved = new TextBlock { Text = "not read", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(saved, "PreviewVolumeSaved");
        var path = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "preview-volume-silence.wav");
        File.WriteAllBytes(path, Silence());

        PreviewPane Pane() => (PreviewPane)((FrameworkElement)frame.Content).FindName("PreviewPanel");
        MediaPlayerElement Player() => (MediaPlayerElement)Pane().FindName("SongPreviewPlayer");
        void Add(string id, string text, RoutedEventHandler action)
        {
            var button = new Button { Content = text };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += action;
            bar.Children.Add(button);
        }
        async Task Load(bool stream)
        {
            Player().AutoPlay = false;
            Pane().Show();
            ((TextBlock)Pane().FindName("PreviewTitleText")).Text = "Silent volume fixture";
            if (stream) Pane().SetPlayerSource(new MemoryStream(File.ReadAllBytes(path), writable: false), "audio/wav");
            else Pane().SetPlayerSource(new Uri(path).AbsoluteUri);
            await Read();
        }
        async Task Read()
        {
            try
            {
                readback.Text = Player().MediaPlayer?.Volume.ToString("F2", CultureInfo.InvariantCulture) ?? "no player";
                saved.Text = (await App.GetService<ILocalSettingsService>().ReadSettingAsync<double?>("preview_player_volume"))
                    ?.ToString("F2", CultureInfo.InvariantCulture) ?? "missing";
            }
            catch (Exception error) { readback.Text = error.GetType().Name; }
        }
        Add("VolumeFixtureUri", "Load URI", async (_, _) => await Load(false));
        Add("VolumeFixtureStream", "Load stream", async (_, _) => await Load(true));
        Add("VolumeFixtureRead", "Read volume", async (_, _) => await Read());
        Add("VolumeFixtureClose", "Close fixture", (_, _) => App.MainWindow.Close());
        bar.Children.Add(readback);
        bar.Children.Add(saved);
        root.Children.Add(bar);
    }

    private static byte[] Silence()
    {
        const int rate = 8000;
        const int length = rate * 2 * 5;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8);
        writer.Write(36 + length);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(length);
        writer.Write(new byte[length]);
        return output.ToArray();
    }
}
