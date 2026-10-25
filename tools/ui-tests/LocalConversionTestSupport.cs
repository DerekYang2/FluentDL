using FluentDL.Contracts.Services;
using FluentDL.Core.Contracts.Services;
using FluentDL.Helpers;
using FluentDL.Services;
using FluentDL.ViewModels;
using FluentDL.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL;

// Only compiled by LocalConversionPreview.targets. No real encoder or provider is reachable through this backend.
internal static class LocalConversionTestSupport
{
    internal static readonly FixtureTime Clock = new();
    private static readonly ControlledConverter Converter = new();
    private static readonly string Folder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "conversion-fixture");

    internal static void Register(IServiceCollection services)
    {
        services.AddSingleton<ILocalFormatConverter>(Converter);
        services.AddSingleton<TimeProvider>(Clock);
    }

    internal static void Attach(UIElement? content)
    {
        var shell = (ShellPage)content!;
        var frame = (Frame)shell.FindName("NavigationFrame");
        var root = (Grid)shell.Content;
        App.MainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 1000));
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 0, 0) };
        Canvas.SetZIndex(bar, 20);
        var ready = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(ready, "ConversionFixtureReady");
        var injected = false;
        LocalExplorerPage Page() => (LocalExplorerPage)frame.Content;
        ReplayGainDialog ReplayGain() => (ReplayGainDialog)Page().FindName("ReplayGainDialog");
        Add(bar, "PrepareConversionFixture", "Prepare conversion", async (_, _) =>
        {
            App.GetService<INavigationService>().NavigateTo(typeof(LocalExplorerViewModel).FullName!);
            await App.GetService<ILocalSettingsService>().SaveSettingAsync(SettingsViewModel.AudioConversionThreads, 1);
            Seed(20, "original");
            var dialog = (ContentDialog)Page().FindName("ConversionDialog");
            ((TextBox)Page().FindName("OutputTextBox")).Text = Folder;
            if (injected) return;
            injected = true;
            var controls = new StackPanel { Spacing = 8 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var status = new TextBlock();
            AutomationProperties.SetAutomationId(status, "ConversionFixtureStatus");
            void Read()
            {
                using var install = ApplicationActivity.Gate.TryEnterInstallation();
                status.Text = $"Started {Converter.Started}, active {Converter.Active}, install {(install == null ? "blocked" : "allowed")}";
            }
            Add(buttons, "FinishConversionFile", "Finish file", (_, _) => Converter.Permits.Release());
            Add(buttons, "AdvanceConversionClock", "Advance clock", (_, _) => Clock.Advance(10));
            Add(buttons, "ReplaceConversionInputs", "Replace input list", (_, _) => Seed(3, "replacement"));
            Add(buttons, "FinishConversionRest", "Finish remaining", (_, _) => Converter.AutoFinish = true);
            Add(buttons, "ReadConversionFixture", "Read state", (_, _) => Read());
            controls.Children.Add(buttons);
            controls.Children.Add(status);
            ((StackPanel)dialog.Content).Children.Insert(1, controls);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => Read();
            dialog.Opened += (_, _) => timer.Start();
            dialog.Closed += (_, _) => timer.Stop();
            Read();
            ready.Text = "ready";
        });
        Add(bar, "ReplayGainAnalysisEtaFixture", "Analysis ETA", async (_, _) => await ReplayGain().ShowEtaFixtureAsync(Page().XamlRoot, false));
        Add(bar, "ReplayGainWritingEtaFixture", "Writing ETA", async (_, _) => await ReplayGain().ShowEtaFixtureAsync(Page().XamlRoot, true));
        Add(bar, "CloseConversionFixture", "Close fixture", (_, _) => App.MainWindow.Close());
        bar.Children.Add(ready);
        root.Children.Add(bar);
    }

    private static void Add(Panel panel, string id, string text, RoutedEventHandler action)
    {
        var button = new Button { Content = text };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += action;
        panel.Children.Add(button);
    }

    private static void Seed(int count, string prefix)
    {
        Directory.CreateDirectory(Folder);
        LocalExplorerViewModel.ClearSongs();
        for (var index = 0; index < count; index++)
        {
            var path = Path.Combine(Folder, $"{prefix}-{index:D2}.wav");
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write("RIFF"u8); writer.Write(36 + 1600); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(1600); writer.Write(new byte[1600]);
            }
            var song = LocalExplorerViewModel.ParseFile(path) ?? throw new InvalidOperationException("The fixture WAV could not be read.");
            LocalExplorerViewModel.AddSong(song);
        }
    }

    internal sealed class FixtureTime : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        internal void Advance(double seconds) => Interlocked.Add(ref ticks, TimeSpan.FromSeconds(seconds).Ticks);
    }

    private sealed class ControlledConverter : ILocalFormatConverter
    {
        internal readonly SemaphoreSlim Permits = new(0);
        internal volatile bool AutoFinish;
        internal int Started;
        internal int Active;
        public string? Convert(string path, string outputDirectory, int format, int quality)
        {
            Interlocked.Increment(ref Started);
            Interlocked.Increment(ref Active);
            try
            {
                if (!AutoFinish && !Permits.Wait(TimeSpan.FromSeconds(90))) throw new TimeoutException("The conversion fixture was not released.");
                var output = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(path) + ".flac");
                File.WriteAllText(output, "synthetic encoder output");
                return output;
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
}
