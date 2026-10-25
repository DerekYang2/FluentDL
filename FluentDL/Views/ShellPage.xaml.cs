using FluentDL.Contracts.Services;
using FluentDL.Helpers;
using FluentDL.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Shell;
using Microsoft.UI.Dispatching;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace FluentDL.Views;

// TODO: Update NavigationViewItem titles and icons in ShellPage.xaml.
public sealed partial class ShellPage : Page
{
    internal static Uri HelpUri { get; } = new("https://github.com/DerekYang2/FluentDL/wiki");
    internal Func<Uri, Task<bool>> OpenHelpAsync { get; set; } = async uri => await Launcher.LaunchUriAsync(uri);
    private bool openingHelp;
    public ShellViewModel ViewModel
    {
        get;
    }


    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        AutomationProperties.SetName(HelpNavigationItem, "Shell_Help_Name".GetLocalized());
        AutomationProperties.SetHelpText(HelpNavigationItem, "Shell_Help_Tooltip".GetLocalized());
        ToolTipService.SetToolTip(HelpNavigationItem, "Shell_Help_Tooltip".GetLocalized());

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(AppTitleBar);
        App.MainWindow.Activated += MainWindow_Activated;
        AppTitleBarText.Text = "AppDisplayName".GetLocalized();
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        TitleBarHelper.UpdateTitleBar(RequestedTheme);

        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
        KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        App.AppTitlebar = AppTitleBarText as UIElement;
    }

    private void NavigationViewControl_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        AppTitleBar.Margin = new Thickness() { Left = sender.CompactPaneLength * (sender.DisplayMode == NavigationViewDisplayMode.Minimal ? 2 : 1), Top = AppTitleBar.Margin.Top, Right = AppTitleBar.Margin.Right, Bottom = AppTitleBar.Margin.Bottom };
    }

    private async void NavigationViewControl_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked || args.InvokedItemContainer != HelpNavigationItem || openingHelp) return;
        openingHelp = true;
        try
        {
            var launched = false;
            try { launched = await OpenHelpAsync(HelpUri); }
            catch (Exception error) { Serilog.Log.Warning(error, "Could not open the FluentDL wiki"); }
            if (launched || XamlRoot == null) return;

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
                Title = "Shell_Help_ErrorTitle".GetLocalized(),
                Content = "Shell_Help_ErrorMessage".GetLocalized(),
                CloseButtonText = "Shell_Help_Close".GetLocalized(),
            };
            AutomationProperties.SetAutomationId(dialog, "HelpLaunchError");
            try { await dialog.ShowAsync(); }
            catch (Exception error) { Serilog.Log.Debug(error, "The help error dialog could not be shown"); }
        }
        finally { openingHelp = false; }
    }

    private static KeyboardAccelerator BuildKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers? modifiers = null)
    {
        var keyboardAccelerator = new KeyboardAccelerator() { Key = key };

        if (modifiers.HasValue)
        {
            keyboardAccelerator.Modifiers = modifiers.Value;
        }

        keyboardAccelerator.Invoked += OnKeyboardAcceleratorInvoked;

        return keyboardAccelerator;
    }

    private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var navigationService = App.GetService<INavigationService>();

        var result = navigationService.GoBack();

        args.Handled = result;
    }
}
