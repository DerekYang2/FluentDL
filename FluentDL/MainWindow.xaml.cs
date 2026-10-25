using FluentDL.Helpers;
using FluentDL.ViewModels;
using Windows.UI.ViewManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using RestSharp;
using System.Text.Json;
using Microsoft.UI.Composition.SystemBackdrops;
using System.Runtime.InteropServices; // For DllImport
using WinRT; // required to support Window.As<ICompositionSupportsSystemBackdrop>()
using Microsoft.UI.Windowing;
using Microsoft.UI;
using WinRT.Interop;
using Windows.UI;


namespace FluentDL;

public sealed partial class MainWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;

    private UISettings settings;
    private bool closeRequested;
    private bool closeReady;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Content = null;
        Title = "AppDisplayName".GetLocalized();

        // Theme change code picked from https://github.com/microsoft/WinUI-Gallery/pull/1239
        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged; // cannot use FrameworkElement.ActualThemeChanged event
        AppWindow.Closing += AppWindow_Closing;
        Closed += async (_, _) =>
        {
            settings.ColorValuesChanged -= Settings_ColorValuesChanged;
            try { await QueueViewModel.ShutdownAsync(); }
            catch (Exception error) { Serilog.Log.Error(error, "Queue shutdown failed"); }
        };
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closeReady) return;
        args.Cancel = true;
        if (closeRequested) return;
        closeRequested = true;
        try { await QueueViewModel.ShutdownAsync(); }
        catch (Exception error) { Serilog.Log.Error(error, "Queue metadata could not be flushed before closing"); }
        finally { closeReady = true; Close(); }
    }

    // this handles updating the caption button colors correctly when indows system theme is changed
    // while the app is open
    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        // This calls comes off-thread, hence we will need to dispatch it to current app's thread
        dispatcherQueue.TryEnqueue(() =>
        {
            TitleBarHelper.ApplySystemThemeToCaptionButtons();
        });
    }
}
