using FluentDL.Activation;
using FluentDL.Contracts.Services;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Services;
using FluentDL.Core.Contracts.Services;
using FluentDL.Helpers;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;
using FluentDL.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FluentDL.Services;

public class ActivationService : IActivationService
{
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILogger<ActivationService> _logger;
    private UIElement? _shell = null;

    public ActivationService(ActivationHandler<LaunchActivatedEventArgs> defaultHandler, IEnumerable<IActivationHandler> activationHandlers, IThemeSelectorService themeSelectorService, ILogger<ActivationService> logger)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _logger = logger;
    }

    public async Task ActivateAsync(object activationArgs)
    {
        var timer = Stopwatch.StartNew();
        _logger.LogInformation("Application activation started");
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
        await InitAPIs(splashScreen);
        if (QueueViewModel.IsClosing) return;

        // Switch from splash screen to shell
        App.MainWindow.Content = _shell ?? new Frame();
        App.MainWindow.Activate();
#if FLUENTDL_PREVIEW_VOLUME_UI_TESTS
        PreviewVolumeTestSupport.Attach(_shell);
#endif
#if FLUENTDL_LOCAL_CONVERSION_UI_TESTS
        LocalConversionTestSupport.Attach(_shell);
#endif
#if FLUENTDL_PICKER_UI_TESTS
        LocalPickerTestSupport.Attach(_shell);
#endif
#if FLUENTDL_COMPACT_LAYOUT_UI_TESTS
        CompactLayoutTestSupport.Attach(_shell);
#endif
#if FLUENTDL_QUEUE_LOAD_UI_TESTS
        QueueLoadTestSupport.Attach(_shell);
#endif
        QueueViewModel.StartArtworkNetwork();
        App.MainWindow.Closed += (s, e) => QueueSaver.Close();

        // Execute tasks after activation
        await StartupAsync();
        _logger.LogInformation("Main window activated in {ElapsedMilliseconds} ms", timer.ElapsedMilliseconds);
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

    private async Task UpdateYtdlp(ILocalSettingsService localSettings, SplashScreenPage splashScreen, int rowIdx)
    {
        // yt-dlp
        var useYtDlp = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.UseYtdlp);
        var updateYtdlp = false;
        if (useYtDlp)
        {
            updateYtdlp = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AutoUpdateYtdlp);
        }

        if (updateYtdlp)
        {
            try
            {
                splashScreen.SetText("Updating yt-dlp ...", rowIdx);
                _logger.LogInformation("yt-dlp update started");
                var existingPath = await localSettings.ReadSettingAsync<string>(SettingsViewModel.YtdlpPath);
                var output = await YtDlp.UpdateYtdlpAsync(existingPath);
                splashScreen.SetText($"Update complete: {output ?? "null"}", rowIdx);
                _logger.LogInformation("yt-dlp update finished");
            } catch(Exception ex)
            {
                _logger.LogWarning(ex, "yt-dlp automatic update failed; continuing startup");
                splashScreen.SetText("ERROR: yt-dlp Update Failed. Please update manually.", rowIdx);
                await Task.Delay(2000);  // So user can see the message
            }
        }
    }

    private async Task InitAPIs(SplashScreenPage splashScreen)
    {
        try
        {
            try
            {
                // Runs on every launch so settings added by an update also get their defaults.
                await SettingsViewModel.SetMissingDefaults();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save missing setting defaults; continuing startup");
            }

            await App.GetService<QueueDisplaySettings>().LoadAsync();
            await App.GetService<PreviewVolumeSettings>().LoadAsync();
            await QueueViewModel.UpdateShortcutVisibility();
            // Fetch previous command list
            //await LocalCommands.Init();
            // Initialize FFMpeg 
            splashScreen.SetText("Initializing FFmpeg ...");
            _logger.LogDebug("Initializing FFmpeg");
            await FFmpegRunner.Initialize();

            //// Initialize environment variables
            //splashScreen.SetText("Reading Bundled Keys ...");
            //await KeyReader.Initialize();

            // Initialize api objects

            var localSettings = App.GetService<ILocalSettingsService>();
            var sessions = App.GetService<SourceRegistry>().All<ISourceSession>().ToList();

            try
            {
                // One splash row per source that signs in, then yt-dlp. Each session reads its own saved credentials.
                var tasks = sessions.Select((session, row) => Task.Run(async () =>
                {
                    var name = session.Descriptor.DisplayName;
                    splashScreen.SetText($"Initializing {name} ...", row);
                    await session.ConnectAsync();
                    _logger.LogInformation("Provider {Provider} initialization finished; authenticated {IsAuthenticated}", name, session.IsConnected);
                    splashScreen.SetText($"{name} Complete", row);
                })).Append(Task.Run(async () =>
                {
                    await UpdateYtdlp(localSettings, splashScreen, sessions.Count);
                })).ToArray();

                await Task.WhenAll(tasks.Select(t => t.WaitAsync(TimeSpan.FromSeconds(30))));

                
                // Init queue database
                splashScreen.ClearRows();
                splashScreen.SetText("Loading Queue State ...");
                await DatabaseService.InitDatabase();
                await QueueViewModel.LoadSaveQueue();
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Provider initialization exceeded the 30 second timeout; some startup work may be incomplete");
                splashScreen.ClearRows();
                splashScreen.SetText("ERROR: One or more APIs timed out");
                await Task.Delay(2000);  // So user can see the message
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Startup initialization failed; continuing with reduced functionality");
            splashScreen.ClearRows();
            splashScreen.SetText($"ERROR: {e.Message}");
            await Task.Delay(2000);  // So user can see the message
        }

        _ = CleanupTempDirectoryAsync();  // Fire-and-forget cleanup of temp files
    }

    /// <summary>
    /// Cleans up all temporary files created by FluentDL on startup.
    /// </summary>
    public static async Task CleanupTempDirectoryAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var tempBaseDir = Path.Combine(Path.GetTempPath(), "FluentDL");

                if (!Directory.Exists(tempBaseDir))
                {
                    return;
                }

                // Delete all files in the temp directory
                var directoryInfo = new DirectoryInfo(tempBaseDir);

                foreach (var file in directoryInfo.GetFiles())
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Debug(ex, "Skipping temporary file cleanup");
                    }
                }

                // Delete all subdirectories if any exist
                foreach (var dir in directoryInfo.GetDirectories())
                {
                    try
                    {
                        dir.Delete(recursive: true);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Debug(ex, "Skipping temporary folder cleanup");
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Startup temporary-file cleanup failed");
            }
        });
    }
}
