using FluentDL.Activation;
using FluentDL.Contracts.Services;
using FluentDL.Helpers;
using FluentDL.ViewModels;
using FluentDL.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace FluentDL.Services;

public class ActivationService : IActivationService
{
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private UIElement? _shell = null;

    public ActivationService(ActivationHandler<LaunchActivatedEventArgs> defaultHandler, IEnumerable<IActivationHandler> activationHandlers, IThemeSelectorService themeSelectorService)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
    }

    public async Task ActivateAsync(object activationArgs)
    {
        // Show splash screen immediately
        var splashScreen = App.GetService<SplashScreenPage>();
        if (App.MainWindow.Content == null)
        {
            App.MainWindow.Content = splashScreen;
            App.MainWindow.Activate();
        }

        // Execute tasks before activation
        await InitializeAsync();

        // Initialize shell behind splash screen
        _shell = App.GetService<ShellPage>();

        // Handle activation via ActivationHandlers
        await HandleActivationAsync(activationArgs);

        // Initialize APIs
        await InitAPIs();

        // Switch from splash screen to shell
        App.MainWindow.Content = _shell ?? new Frame();
        App.MainWindow.Activate();
        App.MainWindow.Closed += (s, e) => QueueSaver.Close();

        // Execute tasks after activation
        await StartupAsync();
    }

    private async Task HandleActivationAsync(object activationArgs)
    {
        var activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));

        if (activationHandler != null)
        {
            await activationHandler.HandleAsync(activationArgs);
        }

        if (_defaultHandler.CanHandle(activationArgs))
        {
            await _defaultHandler.HandleAsync(activationArgs);
        }
    }

    private async Task InitializeAsync()
    {
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        await Task.CompletedTask;
    }

    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();
        await Task.CompletedTask;
    }

    private async Task InitAPIs()
    {
        if (await SettingsViewModel.GetSetting<bool?>(SettingsViewModel.FirstRun) ?? true)
            await SettingsViewModel.SetMissingDefaults();

        await QueueViewModel.UpdateShortcutVisibility();
        try
        {
            // Fetch previous command list
            await LocalCommands.Init();

            // Initialize FFMpeg 
            await FFmpegRunner.Initialize();

            // Initialize environment variables
            await KeyReader.Initialize();

            // Initialize api objects
            var localSettings = App.GetService<ILocalSettingsService>();

            var qobuzEmail = DPAPIHelper.Decrypt(await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzEmail) ?? "");
            var qobuzPassword = DPAPIHelper.Decrypt(await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzPassword) ?? "");
            var qobuzId = await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzId);
            var qobuzToken = await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzToken);
            var qobuzAppId = await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzAppId);
            var qobuzAppSecret = await localSettings.ReadSettingAsync<string>(SettingsViewModel.QobuzAppSecret);
            var spotifyClientId = await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId);
            var spotifyClientSecret = await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret);
            var deezerArl = await localSettings.ReadSettingAsync<string>(SettingsViewModel.DeezerARL);
            await Task.WhenAll(
                Task.Run(() => QobuzApi.Initialize(qobuzEmail, qobuzPassword, qobuzId, qobuzToken, qobuzAppId, qobuzAppSecret)),
                Task.Run(() => SpotifyApi.Initialize(spotifyClientId, spotifyClientSecret)),
                DeezerApi.InitDeezerClient(deezerArl)
            );
            await QueueViewModel.LoadSaveQueue();

            QueueSaver.Init();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
    }
}
