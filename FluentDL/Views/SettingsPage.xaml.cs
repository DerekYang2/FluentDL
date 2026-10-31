using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Controls;
using CommunityToolkit.WinUI.Helpers;
using FluentDL.Contracts.Services;
using FluentDL.Contracts.Sources;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Helpers;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services;
using FluentDL.Services.Sources;
using FluentDL.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.Logging;
using QobuzApiSharp.Service;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;

namespace FluentDL.Views;

// TODO: Set the URL for your privacy policy by updating SettingsPage_PrivacyTermsLink.NavigateUri in Resources.resw.
public sealed partial class SettingsPage : Page
{
    private DispatcherQueue dispatcher;
    private DispatcherTimer dispatcherTimer;
    private ILocalSettingsService localSettings;
    private readonly SourceRegistry sources = App.GetService<SourceRegistry>();
    private readonly ILogger<SettingsPage> _logger;
    private bool _updatingDiagnostics;
    public AppUpdateViewModel Updates { get; } = App.GetService<AppUpdateViewModel>();

    public SettingsViewModel ViewModel
    {
        get;
    }

    // Frame.Navigate creates pages through their parameterless constructor.
    public SettingsPage() : this(App.GetService<ILogger<SettingsPage>>())
    {
    }

    public SettingsPage(ILogger<SettingsPage> logger)
    {
        _logger = logger;
        localSettings = App.GetService<ILocalSettingsService>();

        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        ClientIdInput.TextChanged += (_, _) => UpdateSpotifySignInAvailability();
        SpotifySecretInput.PasswordChanged += (_, _) => UpdateSpotifySignInAvailability();
        this.Loaded += SettingsPage_Loaded;
        this.Unloaded += (_, _) => _spotifyLogin?.Cancel();
        // WinUI moves focus only to focusable controls, so clicking a card or the page background left a text box
        // focused. The page takes focus itself then, without a focus outline.
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        AddHandler(PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(Page_PointerPressedAnywhere), handledEventsToo: true);

        dispatcher = DispatcherQueue.GetForCurrentThread();
        dispatcherTimer = new DispatcherTimer();
        dispatcherTimer.Tick += dispatcherTimer_Tick;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_requestedSection is { } section) RevealSection(section);
        await InitializeAsync();
    }

    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e) => await Updates.CheckAsync();
    private void CancelAppUpdate_Click(object sender, RoutedEventArgs e) => Updates.Cancel();
    private async void AppUpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (!Updates.IsDownloaded) { await Updates.DownloadAsync(); return; }
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = "Install FluentDL update?",
            Content = "FluentDL will save a queue backup before installation. Windows may close the app. GitHub installations need administrator permission.",
            PrimaryButtonText = "Install update", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        var content = App.MainWindow.Content as Control;
        var enabled = content?.IsEnabled ?? true;
        if (content != null) content.IsEnabled = false;
        try
        {
            await Updates.InstallAsync(ApplicationActivity.Gate, async token =>
            {
                await QueueViewModel.SaveForUpdateAsync(token);
                var backup = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Updates", "queue-before-update-" + Guid.NewGuid().ToString("N") + ".zip");
                await DatabaseService.CreateBackupAtPath(backup);
                token.ThrowIfCancellationRequested();
            });
        }
        finally { if (content != null) content.IsEnabled = enabled; }
    }

    private SettingsSection? _requestedSection;
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _requestedSection = e.Parameter is SettingsSection section ? section : null;
        if (IsLoaded && _requestedSection is { } requested) RevealSection(requested);
    }

    internal void RevealSection(SettingsSection section)
    {
        _requestedSection = section;
        if (section == SettingsSection.Updates) AboutExpander.IsExpanded = true;
        FrameworkElement element = section switch
        {
            SettingsSection.Spotify => SpotifyExpander,
            SettingsSection.Tidal => TidalExpander,
            SettingsSection.Deezer => DeezerExpander,
            SettingsSection.Qobuz => QobuzExpander,
            SettingsSection.Downloads => LocationCard,
            SettingsSection.Lyrics => LyricsExpander,
            SettingsSection.Backups => BackupExpander,
            SettingsSection.FFmpeg => FFmpegExpander,
            SettingsSection.Ytdlp => YtdlpExpander,
            SettingsSection.Updates => AppUpdatesCard,
            _ => throw new ArgumentOutOfRangeException(nameof(section)),
        };
        if (element is SettingsExpander expander) expander.IsExpanded = true;
        UpdateLayout();
        element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
    }

    private async Task InitializeAsync()
    {
        // Set sliders
        // Initial values
        ConversionThreadsCard.Description = $"{(int)Math.Round(ConversionThreadsSlider.Value)} threads";
        CommandThreadsCard.Description = $"{(int)Math.Round(CommandThreadsSlider.Value)} threads";
        AudioConversionThreadsCard.Description = $"{(int)Math.Round(AudioConversionThreadsSlider.Value)} threads";
        BackupRetentionCard.Description = $"Keep the latest {Math.Round(BackupRetentionSlider.Value)} backups";
        // Change event
        ConversionThreadsSlider.ValueChanged += (s, e) =>
        {
            ConversionThreadsCard.Description = $"{(int)Math.Round(ConversionThreadsSlider.Value)} threads";
        };
        CommandThreadsSlider.ValueChanged += (s, e) =>
        {
            CommandThreadsCard.Description = $"{(int)Math.Round(CommandThreadsSlider.Value)} threads";
        };
        AudioConversionThreadsSlider.ValueChanged += (s, e) =>
        {
            AudioConversionThreadsCard.Description = $"{(int)Math.Round(AudioConversionThreadsSlider.Value)} threads";
        };
        BackupRetentionSlider.ValueChanged += (s, e) =>
        {
            BackupRetentionCard.Description = $"Keep the latest {Math.Round(BackupRetentionSlider.Value)} backups";
        };
        ReplayGainOffsetSlider.ValueChanged += (s, e) =>
        {
            ReplayGainOffsetCard.Description = ReplayGainOffsetDescription(ReplayGainOffsetSlider.Value);
        };

        CommandThreadsSlider.Value = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.CommandThreads) ?? 1;
        ConversionThreadsSlider.Value = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.ConversionThreads) ?? 3;
        AudioConversionThreadsSlider.Value = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.AudioConversionThreads) ?? 6;
        BackupRetentionSlider.Value = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.BackupRetentionCount) ?? 5;
        ReplayGainOffsetSlider.Value = await localSettings.ReadSettingAsync<double?>(SettingsViewModel.ReplayGainOffset) ?? 0;
        ReplayGainOffsetCard.Description = ReplayGainOffsetDescription(ReplayGainOffsetSlider.Value);
        ReplayGainAutoToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.ReplayGainAutoApply);
        ReplayGainAlbumToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.ReplayGainAlbumDownloads);
        ReplayGainAlbumCard.IsEnabled = ReplayGainAutoToggle.IsOn;
        ReplayGainClipToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.ReplayGainPreventClipping);

        // Set quality combo boxes (default flac)
        DeezerQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.DeezerQuality) ?? 2;
        QobuzQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.QobuzQuality) ?? 3;
        SpotifyQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.SpotifyQuality) ?? 1;
        YoutubeQualityComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.YoutubeQuality) ?? 1;

        // Set download options
        LocationCard.Description = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.DownloadDirectory) ?? "No folder selected";
        if (string.IsNullOrWhiteSpace(LocationCard.Description.ToString())) LocationCard.Description = "No folder selected";
        SubfoldersToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.Subfolders);
        AlbumSubfoldersToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AlbumSubfolders);
        DownloadAlbumCoverArtToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.DownloadAlbumCoverArt);

        // Set lyric options
        DownloadLyricsToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.DownloadLyrics);
        LyricsPreferenceComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.LyricsPreference) ?? 2;
        LyricsExtensionComboBox.SelectedIndex = await localSettings.ReadSettingAsync<int?>(SettingsViewModel.LyricsExtension) ?? 0;
        var lyricsDir = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.LyricsDirectory);
        LyricsLocationText.Text = string.IsNullOrWhiteSpace(lyricsDir) ? "No folder selected" : lyricsDir;

        // Set wildcards
        SubfolderNameInput.IsEnabled = SubfoldersToggle.IsOn;
        SubfolderInfoBar.IsOpen = !SubfoldersToggle.IsOn;
        AlbumSubfolderNameInput.IsEnabled = AlbumSubfoldersToggle.IsOn;
        AlbumSubfolderInfoBar.IsOpen = !AlbumSubfoldersToggle.IsOn;
        SubfolderNameInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.SubfolderWildcard) ?? "";
        AlbumSubfolderNameInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.AlbumSubfolderWildcard) ?? "";
        FileNameInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.FileWildcard) ?? "";

        // Set FFmpeg path
        FFmpegPathCard.Description = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.FFmpegPath) ?? "No folder selected";
        if (string.IsNullOrWhiteSpace(FFmpegPathCard.Description.ToString())) FFmpegPathCard.Description = "No folder selected";

        // Ytdlp
        var ytdlpPath = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.YtdlpPath);
        YtdlpPathCard.Description = string.IsNullOrWhiteSpace(ytdlpPath) ? "No file selected" : ytdlpPath;
        // fire are forget
        _ = RefreshYtdlpUIAsync(ytdlpPath);

        // Set ToggleSwitches
        AskToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AskBeforeDownload);
        OverwriteToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.Overwrite);
        NotificationsToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.Notifications);
        AutoPlayToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AutoPlay);
        NotifyUpdateToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.NotifyUpdate);
        AutoBackupToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AutoBackupEnabled);
        UseYtdlpToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.UseYtdlp);
        AutoUpdateYtdlpToggle.IsOn = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.AutoUpdateYtdlp);

        // Set Ids/Secrets
        _spotifySavedClientId = (await localSettings.ReadSettingAsync<string?>(SettingsViewModel.SpotifyClientId)) ?? "";
        _spotifySavedClientSecret = (await localSettings.ReadSettingAsync<string?>(SettingsViewModel.SpotifyClientSecret)) ?? "";
        ClientIdInput.Text = _spotifySavedClientId;
        SpotifySecretInput.Password = _spotifySavedClientSecret;
        UpdateSpotifySignInAvailability();
        TidalClientIdInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.TidalClientId) ?? "";
        TidalSecretInput.Password = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.TidalClientSecret) ?? "";
        TidalCountryInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.TidalCountry) ?? "";
        TidalCountryInput.PlaceholderText = $"Windows region ({System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName})";
        DeezerARLInput.Password = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.DeezerARL) ?? "";
        QobuzIDInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzId) ?? "";
        QobuzTokenInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzToken) ?? "";
        //QobuzEmailInput.Text = DPAPIHelper.Decrypt(await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzEmail) ?? "");
        //QobuzPasswordInput.Password = DPAPIHelper.Decrypt(await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzPassword) ?? "");
        QobuzAppIdInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzAppId) ?? "";
        QobuzAppSecretInput.Text = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.QobuzAppSecret) ?? "";

        // Set search checkboxes
        SearchAddCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchAddChecked);
        SearchDownloadCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchDownloadChecked);
        SearchShareCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchShareChecked);
        SearchOpenCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.SearchOpenChecked);

        // Set local explorer checkboxes
        LocalExplorerAddCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.LocalExplorerAddChecked);
        LocalExplorerEditCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.LocalExplorerEditChecked);
        LocalExplorerOpenCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.LocalExplorerOpenChecked);

        // Set queue checkboxes
        QueueShareCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.QueueShareChecked);
        QueueDownloadCoverCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.QueueDownloadCoverChecked);
        QueueRemoveCheckbox.IsChecked = await localSettings.ReadSettingAsync<bool>(SettingsViewModel.QueueRemoveChecked);

        // Auth indicators
        SetDeezerAuthInfo(Session(SourceIds.Deezer).IsConnected);
        SetQobuzAuthInfo(Session(SourceIds.Qobuz).IsConnected);
        SetSpotifyAuthInfo(Session(SourceIds.Spotify).IsConnected);
        SetTidalAuthInfo(Session(SourceIds.Tidal).IsConnected);
        var tidalQuality = await localSettings.ReadSettingAsync<string>(SettingKeys.TidalDownloadQuality) ?? "HI_RES_LOSSLESS";
        TidalDownloadQualityInput.SelectedItem = TidalDownloadQualityInput.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tidalQuality)
            ?? TidalDownloadQualityInput.Items[3];
        TidalDownloadClientIdInput.Text = await localSettings.ReadSettingAsync<string>(SettingKeys.TidalDownloadClientId) ?? "";
        await UpdateTidalDownloadStatusAsync();
        _tidalDownloadSettingsReady = true;
        await InitializeDiagnosticsAsync();
    }

    private async Task InitializeDiagnosticsAsync()
    {
        _updatingDiagnostics = true;
        try
        {
            var enabled = await localSettings.ReadSettingAsync<bool>(DiagnosticLogging.VerboseSettingKey);
            App.SetVerboseLogging(enabled);
            VerboseLoggingToggle.IsOn = enabled;
            VerboseLoggingToggle.IsEnabled = App.LoggingError is null;
            LogsFolderText.Text = App.LogDirectory;
            LoggingStatusBar.Message = App.LoggingError ?? string.Empty;
            LoggingStatusBar.IsOpen = App.LoggingError is not null;
        }
        finally
        {
            _updatingDiagnostics = false;
        }
    }

    private async void VerboseLoggingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingDiagnostics) return;
        var previous = App.IsVerboseLogging;
        var enabled = VerboseLoggingToggle.IsOn;
        VerboseLoggingToggle.IsEnabled = false;
        try
        {
            await localSettings.SaveSettingAsync(DiagnosticLogging.VerboseSettingKey, enabled);
            App.SetVerboseLogging(enabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save verbose logging preference");
            _updatingDiagnostics = true;
            VerboseLoggingToggle.IsOn = previous;
            ShowInfoBar(InfoBarSeverity.Error, "Could not save the logging preference. The previous level is still active.", 5, "Diagnostics");
        }
        finally
        {
            _updatingDiagnostics = false;
            VerboseLoggingToggle.IsEnabled = App.LoggingError is null;
        }
    }

    private async void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(App.LogDirectory);
            if (!await Windows.System.Launcher.LaunchFolderAsync(folder))
            {
                throw new InvalidOperationException("Windows could not open the logs folder.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not open the logs folder");
            ShowInfoBar(InfoBarSeverity.Error, "Could not open the logs folder. You can copy the displayed path.", 5, "Diagnostics");
        }
    }

    private void Page_PointerPressedAnywhere(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Runs after the clicked control's own handling: a button or combo box has already taken focus by now.
        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is not (TextBox or PasswordBox)) return;
        for (var element = e.OriginalSource as DependencyObject; element != null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is TextBox or PasswordBox) return; // a click inside a text box keeps its focus
        }
        Focus(FocusState.Programmatic);
    }

    private void SetDeezerAuthInfo(bool isInitialized)
    {
        DeezerInfoText.Text = isInitialized
            ? "Authentication: ARL session\n" + (Session(SourceIds.Deezer).StatusText ?? "Signed in")
            : "Account: not signed in\nAuthentication method: ARL session";
        UpdateProviderCapabilities();
    }

    private void SetQobuzAuthInfo(bool isInitialized)
    {
        QobuzInfoText.Text = isInitialized
            ? "Authentication: user ID and token\n" + (Session(SourceIds.Qobuz).StatusText ?? "Signed in")
            : "Account: not signed in\nAuthentication method: user ID and token";
        UpdateProviderCapabilities();
    }

    private void SetSpotifyAuthInfo(bool isInitialized)
    {
        var source = (SpotifySource)Session(SourceIds.Spotify);
        var userSignedIn = source.IsSignedInForPlaylists;
        if (_spotifyLogin == null)
            SpotifySignInButton.Content = userSignedIn ? "Sign out" : "Sign in";
        SpotifyInfoText.Text = FormatSpotifyAccountInfo(isInitialized ? source.StatusText ?? "Connected" : "Not connected",
            userSignedIn ? source.UserAccountId ?? "Signed in" : null);
        UpdateSpotifySignInAvailability();
        UpdateProviderCapabilities();
    }

    internal static string FormatSpotifyAccountInfo(string catalogIdentity, string? userAccount) =>
        $"Catalog access:\n{catalogIdentity}\n\nUser account:\n{userAccount ?? "Not signed in"}";

    private async void SpotifyUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_spotifySaving || _spotifyLogin != null) return;
        var id = ClientIdInput.Text.Trim();
        var secret = SpotifySecretInput.Password.Trim();
        _spotifySaving = true;
        SpotifyUpdateButton.IsEnabled = false;
        UpdateSpotifySignInAvailability();
        try
        {
            if (id != _spotifySavedClientId || secret != _spotifySavedClientSecret)
                await ((SpotifySource)Session(SourceIds.Spotify)).ClearUserAuthorizationAsync();
            await localSettings.SaveSettingAsync(SettingsViewModel.SpotifyClientId, id);
            await localSettings.SaveSettingAsync(SettingsViewModel.SpotifyClientSecret, secret);
            _spotifySavedClientId = id;
            _spotifySavedClientSecret = secret;
            ShowConnectResult(await Session(SourceIds.Spotify).ConnectAsync(), "Spotify", SetSpotifyAuthInfo);
        }
        catch (Exception)
        {
            _spotifySavedClientId = "";
            _spotifySavedClientSecret = "";
            ShowInfoBar(InfoBarSeverity.Error, "Could not save Spotify credentials.", 5, "Spotify");
        }
        finally
        {
            _spotifySaving = false;
            SpotifyUpdateButton.IsEnabled = true;
            UpdateSpotifySignInAvailability();
        }
    }

    private string _spotifySavedClientId = "";
    private string _spotifySavedClientSecret = "";
    private bool _spotifySaving;

    internal static bool SpotifyCredentialsMatch(string? savedId, string? savedSecret, string enteredId, string enteredSecret) =>
        !string.IsNullOrWhiteSpace(savedId) && !string.IsNullOrWhiteSpace(savedSecret)
        && savedId == enteredId.Trim() && savedSecret == enteredSecret.Trim();

    private void UpdateSpotifySignInAvailability()
    {
        SpotifySignInButton.IsEnabled = !_spotifySaving && (_spotifyLogin != null
            || ((SpotifySource)Session(SourceIds.Spotify)).IsSignedInForPlaylists
            || SpotifyCredentialsMatch(_spotifySavedClientId, _spotifySavedClientSecret, ClientIdInput.Text, SpotifySecretInput.Password));
    }

    private CancellationTokenSource? _spotifyLogin;
    private async void SpotifySignInButton_Click(object sender, RoutedEventArgs e)
    {
        if (_spotifyLogin != null) { _spotifyLogin.Cancel(); return; }
        var source = (SpotifySource)Session(SourceIds.Spotify);
        if (source.IsSignedInForPlaylists)
        {
            try
            {
                await source.ClearUserAuthorizationAsync();
                ShowInfoBar(InfoBarSeverity.Informational, "Signed out of Spotify playlist access.", 5, "Spotify");
            }
            catch (Exception)
            {
                ShowInfoBar(InfoBarSeverity.Error, "Signed out for this session, but FluentDL could not clear the saved Spotify sign-in. It may return after restarting the app.", 10, "Spotify");
            }
            SetSpotifyAuthInfo(source.IsConnected);
            return;
        }
        var id = await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientId);
        var secret = await localSettings.ReadSettingAsync<string>(SettingsViewModel.SpotifyClientSecret);
        if (_spotifySaving || !SpotifyCredentialsMatch(id, secret, ClientIdInput.Text, SpotifySecretInput.Password))
        {
            _spotifySavedClientId = id ?? "";
            _spotifySavedClientSecret = secret ?? "";
            UpdateSpotifySignInAvailability();
            return;
        }
        using var cancellation = _spotifyLogin = new CancellationTokenSource();
        SpotifySignInButton.Content = "Cancel sign-in";
        SpotifyUpdateButton.IsEnabled = false;
        ShowInfoBar(InfoBarSeverity.Informational, "Complete authorization in your browser. For Development Mode, this Spotify account must be in Developer Dashboard > your app > Settings > Users Management.", 15, "Spotify");
        try
        {
            var ready = await source.AuthorizePlaylistAsync(cancellation.Token);
            ShowInfoBar(ready ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                ready ? "Spotify user sign-in verified. Private playlists and playlist uploads are available."
                    : "Spotify sign-in could not be verified. Check the saved credentials, redirect URI and Users Management allowlist. Browser consent alone is not enough.", 10, "Spotify");
        }
        catch (OperationCanceledException) { ShowInfoBar(InfoBarSeverity.Informational, "Spotify sign-in cancelled.", 4, "Spotify"); }
        finally
        {
            _spotifyLogin = null;
            SpotifyUpdateButton.IsEnabled = true;
            SetSpotifyAuthInfo(source.IsConnected);
        }
    }

    private CancellationTokenSource? _tidalLogin;
    private bool _tidalDownloadSettingsReady;
    private string? _tidalDownloadAccount;
    private bool _tidalDownloadSaved;
    private string _tidalDownloadSubscription = "Unavailable";
    private Services.Tidal.TidalDownloadClient TidalDownloads => App.GetService<Services.Tidal.TidalDownloadClient>();

    private async Task UpdateTidalDownloadStatusAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            _tidalDownloadAccount = await TidalDownloads.GetAccountNameAsync(timeout.Token);
            _tidalDownloadSubscription = await TidalDownloads.GetSubscriptionStatusAsync(timeout.Token);
        }
        catch (Exception)
        {
            // Keep account emails out of logs and retain the last display identity after a temporary network failure.
            Serilog.Log.Information("TIDAL download account identity could not be restored");
            _tidalDownloadSubscription = "Unavailable";
        }
        var saved = _tidalDownloadSaved = await TidalDownloads.HasSessionAsync();
        if (!saved) _tidalDownloadAccount = null;
        TidalDownloadStatus.Text = saved
            ? $"{_tidalDownloadAccount ?? "Saved account"}  ·  Subscription: {_tidalDownloadSubscription}"
            : "Not signed in";
        TidalDownloadStatusBox.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        TidalDownloadLoginButton.Content = saved ? "Sign out" : "Sign in";
        TidalDownloadLoginButton.IsEnabled = true;
        SetTidalAuthInfo(Session(SourceIds.Tidal).IsConnected);
    }

    private async void TidalDownloadQuality_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_tidalDownloadSettingsReady || TidalDownloadQualityInput.SelectedItem is not ComboBoxItem item) return;
        try { await localSettings.SaveSettingAsync(SettingKeys.TidalDownloadQuality, (string)item.Tag); }
        catch (Exception) { ShowInfoBar(InfoBarSeverity.Error, "Couldn't save the TIDAL quality.", 5, "TIDAL"); }
    }

    private async void TidalDownloadLogin_Click(object sender, RoutedEventArgs e)
    {
        TidalDownloadLoginButton.IsEnabled = false;
        try
        {
            if (await TidalDownloads.HasSessionAsync())
            {
                var account = _tidalDownloadAccount ?? "the download account";
                await TidalDownloads.LogoutAsync();
                await Session(SourceIds.Tidal).ConnectAsync();
                ShowInfoBar(InfoBarSeverity.Informational, $"Signed out of TIDAL downloads for {account}.", 4, "TIDAL");
            }
            else
            {
                var login = await TidalDownloads.BeginLoginAsync();
                var dialog = new TidalDownloadLoginDialog(TidalDownloads, login) { XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
                await dialog.ShowAsync();
                if (dialog.SignedIn)
                {
                    await Session(SourceIds.Tidal).ConnectAsync();
                    await UpdateTidalDownloadStatusAsync();
                    ShowInfoBar(InfoBarSeverity.Success, $"Signed in for TIDAL downloads as {_tidalDownloadAccount ?? "TIDAL account"}.", 5, "TIDAL");
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning("TIDAL download login dialog failed: {ExceptionType}, HRESULT {HResult}",
                ex.GetType().FullName, $"0x{ex.HResult:X8}");
            ShowInfoBar(InfoBarSeverity.Error, ex is InvalidOperationException ? ex.Message
                : $"Couldn't start the TIDAL download sign-in. Error 0x{ex.HResult:X8}.", 5, "TIDAL");
        }
        finally { await UpdateTidalDownloadStatusAsync(); }
    }

    // API account sign-in (PKCE) is off for now. See TidalApi.AccountLoginEnabled to bring it back with this handler.
    // // Sign in, cancel a sign-in in progress, or sign out, depending on the state.
    // private async void TidalLoginButton_Click(object sender, RoutedEventArgs e)
    // {
    //     var login = (IAccountLogin)Session(SourceIds.Tidal);
    //     if (_tidalLogin != null)
    //     {
    //         _tidalLogin.Cancel();
    //         return;
    //     }
    //     if (login.AccountName != null)
    //     {
    //         await login.LogoutAsync();
    //         ShowInfoBar(InfoBarSeverity.Informational, "Signed out of TIDAL.", 3, "TIDAL");
    //         SetTidalAuthInfo(Session(SourceIds.Tidal).IsConnected);
    //         return;
    //     }

    //     _tidalLogin = new CancellationTokenSource();
    //     TidalLoginButton.Content = "Cancel sign-in";
    //     ShowInfoBar(InfoBarSeverity.Informational, "Finish signing in to TIDAL in your browser.", 5, "TIDAL");
    //     try
    //     {
    //         var result = await login.LoginAsync(_tidalLogin.Token);
    //         ShowConnectResult(result, "TIDAL", _ => SetTidalAuthInfo(Session(SourceIds.Tidal).IsConnected));
    //     }
    //     finally
    //     {
    //         _tidalLogin.Dispose();
    //         _tidalLogin = null;
    //         SetTidalAuthInfo(Session(SourceIds.Tidal).IsConnected);
    //     }
    // }

    private void SetTidalAuthInfo(bool isInitialized)
    {
        var catalogStatus = isInitialized ? Session(SourceIds.Tidal).StatusText ?? "Connected" : "Not connected to the API.";
        TidalInfoText.Text = FormatTidalAccountInfo(catalogStatus, _tidalDownloadAccount, _tidalDownloadSaved, _tidalDownloadSubscription);
        UpdateProviderCapabilities();
    }

    private void UpdateProviderCapabilities()
    {
        var deezer = (DeezerSource)Session(SourceIds.Deezer);
        var qobuz = (QobuzSource)Session(SourceIds.Qobuz);
        var spotify = (SpotifySource)Session(SourceIds.Spotify);
        var qobuzAccess = qobuz.StreamingAccess;
        ViewModel.UpdateProviderCapabilities(new(
            DeezerSignedIn: deezer.IsConnected, DeezerLossless: deezer.IsPremium,
            DeezerPlaylistReadAccess: deezer.PlaylistReadAccess,
            QobuzCatalog: qobuz.HasCatalogAccess, QobuzSignedIn: qobuz.IsConnected,
            QobuzFullTracks: qobuzAccess.FullTracks, QobuzLossless: qobuzAccess.Lossless,
            SpotifyCatalog: spotify.IsConnected, SpotifySignedIn: spotify.IsSignedInForPlaylists,
            TidalCatalog: Session(SourceIds.Tidal).IsConnected, TidalSignedIn: _tidalDownloadSaved,
            TidalSubscription: _tidalDownloadSubscription switch { "Active" => true, "Not active" => false, _ => null }));
    }

    public static Visibility CapabilityVisible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    internal static string FormatTidalAccountInfo(string apiIdentity, string? downloadAccount, bool downloadSaved, string subscription) =>
        $"Catalog access:\n{apiIdentity}\n\nUser account:\n{(downloadSaved ? downloadAccount ?? "Saved account" : "Not signed in")}\nSubscription: {(downloadSaved ? subscription : "Not signed in")}";

    private async void TidalUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var downloadClientId = TidalDownloadClientIdInput.Text.Trim();
        if (downloadClientId.Length != 0 && !Services.Tidal.TidalDownloadClient.IsValidClientId(downloadClientId))
        {
            ShowInfoBar(InfoBarSeverity.Warning, "The download client ID can only contain letters, numbers, hyphens and underscores.", 5, "TIDAL");
            return;
        }
        await localSettings.SaveSettingAsync(SettingKeys.TidalDownloadClientId, downloadClientId.Length == 0 ? null : downloadClientId);
        await localSettings.SaveSettingAsync(SettingsViewModel.TidalClientId, TidalClientIdInput.Text.Trim());
        await localSettings.SaveSettingAsync(SettingsViewModel.TidalClientSecret, TidalSecretInput.Password.Trim());
        await localSettings.SaveSettingAsync(SettingsViewModel.TidalCountry, TidalCountryInput.Text.Trim().ToUpperInvariant());
        // Missing credentials come back as a warning, not an error, so ask the session whether it signed in.
        ShowConnectResult(await Session(SourceIds.Tidal).ConnectAsync(), "TIDAL", _ => SetTidalAuthInfo(Session(SourceIds.Tidal).IsConnected));
    }

    private async void DeezerUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.DeezerARL, DeezerARLInput.Password.Trim());
        ShowConnectResult(await Session(SourceIds.Deezer).ConnectAsync(), "Deezer", SetDeezerAuthInfo);
    }

    private async void QobuzUpdateButton_Click(object sender, RoutedEventArgs e) {
        //await localSettings.SaveSettingAsync(SettingsViewModel.QobuzEmail, DPAPIHelper.Encrypt(QobuzEmailInput.Text.Trim()));
        //await localSettings.SaveSettingAsync(SettingsViewModel.QobuzPassword, DPAPIHelper.Encrypt(QobuzPasswordInput.Password.Trim()));
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzId, QobuzIDInput.Text.Trim());
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzToken, QobuzTokenInput.Text.Trim());
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzAppId, QobuzAppIdInput.Text.Trim());
        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzAppSecret, QobuzAppSecretInput.Text.Trim());
        ShowConnectResult(await Session(SourceIds.Qobuz).ConnectAsync(), "Qobuz", SetQobuzAuthInfo);
    }

    private ISourceSession Session(string id) =>
        sources.TryGet<ISourceSession>(id, out var session) ? session : throw new InvalidOperationException($"No session for {id}.");

    // Shows the sign-in message, if the source reported one, and updates that source's login details.
    private void ShowConnectResult(ConnectResult result, string title, Action<bool> setAuthInfo)
    {
        if (result.Message == null)
        {
            return;
        }
        ShowInfoBar(result.Level.ToSeverity(), result.Message, 3, title);
        setAuthInfo(result.Level != StatusLevel.Error);
    }

    private async void SelectFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        FolderPicker openPicker = new Windows.Storage.Pickers.FolderPicker();

        // Retrieve the window handle of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

        // Initialize the folder picker with the window handle.
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        openPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        openPicker.FileTypeFilter.Add("*");

        // Open the picker for the user to pick a folder
        StorageFolder folder = await openPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("PickedFolderToken", folder); // Save the folder for future access

            if (Directory.Exists(folder.Path) && Path.IsPathRooted(folder.Path))
            {
                // Set the folder path in the text box
                LocationCard.Description = folder.Path;
                // Save to settings
                await localSettings.SaveSettingAsync<string?>(SettingsViewModel.DownloadDirectory, folder.Path);
                ShowInfoBar(InfoBarSeverity.Success, StatusMarkup.Format($"Set download directory to <a href='{folder.Path}'>{folder.Path}</a>"), isMarkup: true);
            }
            else
            {
                ShowInfoBar(InfoBarSeverity.Error, "Invalid folder path");
            }
        }
    }

    private async void SelectFFmpegButton_OnClick(object sender, RoutedEventArgs e)
    {
        FolderPicker openPicker = new Windows.Storage.Pickers.FolderPicker();

        // Retrieve the window handle of the current WinUI 3 window.
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

        // Initialize the folder picker with the window handle.
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        openPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        openPicker.FileTypeFilter.Add("*");

        // Open the picker for the user to pick a folder
        StorageFolder folder = await openPicker.PickSingleFolderAsync();
        if (folder != null)
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace("PickedFolderToken", folder); // Save the folder for future access

            if (Directory.Exists(folder.Path) && Path.IsPathRooted(folder.Path))
            {
                // Set the folder path in the text box
                FFmpegPathCard.Description = folder.Path;
                await localSettings.SaveSettingAsync(SettingsViewModel.FFmpegPath, folder.Path);
                // Update ffmpeg runner
                await FFmpegRunner.Initialize();

                ShowInfoBar(InfoBarSeverity.Success, StatusMarkup.Format($"Set FFmpeg path to <a href='{folder.Path}'>{folder.Path}</a>"), isMarkup: true);
            }
            else
            {
                FFmpegPathCard.Description = "Invalid folder path";

                ShowInfoBar(InfoBarSeverity.Error, "Invalid folder path");
            }
        }
    }

    private async Task RefreshYtdlpUIAsync(string? executablePath)
    {
        try
        {
            bool hasCustomExecutable = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath);

            // Grey out update button if no custom executable is attached
            UpdateYtdlpButton.IsEnabled = hasCustomExecutable;

            if (hasCustomExecutable)
            {
                string? version = await YtDlp.GetYtdlpVersionAsync(executablePath);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    YtdlpVersionText.Text = version;
                    YtdlpVersionText.Visibility = Visibility.Visible;
                    return;
                }
            }

        }
        catch
        {
            // Collapse if no executable is attached or version check fails
            YtdlpVersionText.Visibility = Visibility.Collapsed;
        }
    }

    private async void SelectYtdlpButton_OnClick(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        openPicker.FileTypeFilter.Add(".exe");

        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        var file = await openPicker.PickSingleFileAsync();
        if (file == null)
        {
            return;
        }

        YtdlpPathCard.Description = file.Path;
        await localSettings.SaveSettingAsync(SettingsViewModel.YtdlpPath, file.Path);

        // Refresh the UI with the new executable
        await RefreshYtdlpUIAsync(file.Path);

        ShowInfoBar(InfoBarSeverity.Success, StatusMarkup.Format($"Set yt-dlp path to <a href='{file.Path}'>{file.Path}</a>"), isMarkup: true);
    }

    private async void UpdateYtdlpButton_OnClick(object sender, RoutedEventArgs e)
    {
        var ytdlpPath = await localSettings.ReadSettingAsync<string?>(SettingsViewModel.YtdlpPath);

        if (string.IsNullOrWhiteSpace(ytdlpPath) || !File.Exists(ytdlpPath)) return;

        try
        {
            // Disable temporarily while updating
            UpdateYtdlpButton.IsEnabled = false;
            UpdateYtdlpButton.Content = "Updating...";

            string? output = await YtDlp.UpdateYtdlpAsync(ytdlpPath);

            // Output format is usually "yt-dlp is up to date" or "Updated yt-dlp to version X"
            ShowInfoBar(InfoBarSeverity.Success, output ?? "Update finished silently.", seconds: 10);

            // Refresh version display after update
            await RefreshYtdlpUIAsync(ytdlpPath);
        }
        catch (Exception ex)
        {
            ShowInfoBar(InfoBarSeverity.Error, $"Failed to update yt-dlp: {ex.Message}", seconds: 10);
        }
        finally
        {
            UpdateYtdlpButton.Content = "Update";
            UpdateYtdlpButton.IsEnabled = true;
        }
    }

    private async void AskToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.AskBeforeDownload, isToggled);
    }

    private void OverwriteToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        localSettings.SaveSettingAsync(SettingsViewModel.Overwrite, isToggled);
    }

    private async void DeezerQualityComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 0 -> MP3 128kbps
        // 1 -> MP3 320kbps
        // 2 -> FLAC 1411kbps

        await localSettings.SaveSettingAsync(SettingsViewModel.DeezerQuality, DeezerQualityComboBox.SelectedIndex);
    }

    private async void QobuzQualityComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        /*
           "5" -> MP3 320kbps CBR
           "6" -> FLAC 16bit/44.1kHz
           "7" -> FLAC 24bit/96kHz
           "27" -> FLAC 24bit/192kHz
         */

        await localSettings.SaveSettingAsync(SettingsViewModel.QobuzQuality, QobuzQualityComboBox.SelectedIndex);
    }

    private async void YoutubeQualityComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 0 - opus
        // 1 - flac (opus)
        // 2 - m4a (aac)

        await localSettings.SaveSettingAsync(SettingsViewModel.YoutubeQuality, YoutubeQualityComboBox.SelectedIndex);
    }

    private async void SpotifyQualityComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 0 - spotify mp3
        // 1 - spotify flac
        await localSettings.SaveSettingAsync(SettingsViewModel.SpotifyQuality, SpotifyQualityComboBox.SelectedIndex);
    }

    private void ComboBox_OnDropDownOpened(object? sender, object e)
    {
        var comboBox = sender as ComboBox;
        if (comboBox == null || comboBox.SelectedItem == null)
        {
            return;
        }

        if (comboBox.SelectedItem is ComboBoxItem item) // Set placeholder to string to prevent collapse
        {
            comboBox.PlaceholderText = item.Content.ToString();
        }
    }

    // Save the value when the slider loses focus
    private async void CommandThreadsSlider_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null)
        {
            return;
        }

        var value = (int)slider.Value;
        await localSettings.SaveSettingAsync(SettingsViewModel.CommandThreads, value);
    }

    private async void ConversionThreadsSlider_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null)
        {
            return;
        }

        var value = (int)slider.Value;
        await localSettings.SaveSettingAsync(SettingsViewModel.ConversionThreads, value);
    }

    private void AudioConversionThreadsSlider_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null)
        {
            return;
        }

        var value = (int)slider.Value;
        localSettings.SaveSettingAsync(SettingsViewModel.AudioConversionThreads, value);
    }

    private async void BackupRetentionSlider_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider == null)
        {
            return;
        }

        var value = (int)slider.Value;
        await localSettings.SaveSettingAsync(SettingsViewModel.BackupRetentionCount, value);
    }

    private async void ReplayGainOffsetSlider_OnLostFocus(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.ReplayGainOffset, ReplayGainOffsetSlider.Value);
    }

    private static string ReplayGainOffsetDescription(double offset) => $"Shift every calculated gain by {offset:+0.0;-0.0;0.0} dB";

    private async void ReplayGainAutoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        ReplayGainAlbumCard.IsEnabled = ReplayGainAutoToggle.IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.ReplayGainAutoApply, ReplayGainAutoToggle.IsOn);
    }

    private async void ReplayGainAlbumToggle_Toggled(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.ReplayGainAlbumDownloads, ReplayGainAlbumToggle.IsOn);
    }

    private async void ReplayGainClipToggle_Toggled(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.ReplayGainPreventClipping, ReplayGainClipToggle.IsOn);
    }

    private async void Search_OnChecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var index = checkBox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchAddChecked, true);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchDownloadChecked, true);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchShareChecked, true);
                break;
            case "3":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchOpenChecked, true);
                break;
        }
    }

    private async void Search_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var index = checkBox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchAddChecked, false);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchDownloadChecked, false);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchShareChecked, false);
                break;
            case "3":
                await localSettings.SaveSettingAsync(SettingsViewModel.SearchOpenChecked, false);
                break;
        }
    }

    private async void LocalExplorer_OnChecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var index = checkBox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerAddChecked, true);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerEditChecked, true);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerOpenChecked, true);
                break;
        }
    }

    private async void LocalExplorer_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var index = checkBox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerAddChecked, false);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerEditChecked, false);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.LocalExplorerOpenChecked, false);
                break;
        }
    }

    private async void Queue_OnChecked(object sender, RoutedEventArgs e)
    {
        var checkbox = sender as CheckBox;
        var index = checkbox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueShareChecked, true);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueDownloadCoverChecked, true);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueRemoveChecked, true);
                break;
        }
    }

    private async void Queue_OnUnchecked(object sender, RoutedEventArgs e)
    {
        var checkbox = sender as CheckBox;
        var index = checkbox.Tag.ToString();

        switch (index)
        {
            case "0":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueShareChecked, false);
                break;
            case "1":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueDownloadCoverChecked, false);
                break;
            case "2":
                await localSettings.SaveSettingAsync(SettingsViewModel.QueueRemoveChecked, false);
                break;
        }
    }

    private async void NotificationsToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.Notifications, isToggled);
    }
    private async void AutoPlayToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.AutoPlay, isToggled);
    }

    private async void AutoBackupToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.AutoBackupEnabled, isToggled);
    }
    private async void NotifyUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.NotifyUpdate, isToggled);
    }

    private async void DownloadLyricsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch).IsOn;
        await localSettings.SaveSettingAsync(SettingsViewModel.DownloadLyrics, isToggled);
    }

    private async void LyricsPreferenceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var comboBox = sender as ComboBox;
        if (comboBox?.SelectedItem is ComboBoxItem item)
        {
            await localSettings.SaveSettingAsync(SettingsViewModel.LyricsPreference, comboBox.SelectedIndex);
        }
    }

    private async void LyricsExtensionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 0 - .lrc
        // 1 - .txt
        await localSettings.SaveSettingAsync(SettingsViewModel.LyricsExtension, LyricsExtensionComboBox.SelectedIndex);
    }

    private async void SelectLyricsDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = await StoragePickerHelper.PickFolderAsync(PickerLocationId.MusicLibrary);
        if (folder != null)
        {
            LyricsLocationText.Text = folder.Path;
            await localSettings.SaveSettingAsync(SettingsViewModel.LyricsDirectory, folder.Path);
        }
    }

    private async void ResetLyricsDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        LyricsLocationText.Text = "No folder selected";
        await localSettings.SaveSettingAsync(SettingsViewModel.LyricsDirectory, "");
    }

    private void SpotifyRedirectUriCopyButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.CopyToClipboard(ViewModel.SpotifyCallbackUri);
        ShowInfoBar(InfoBarSeverity.Success, "Copied redirect URI.", 2, "Spotify");
    }

    private async void ResetFFmpegButton_OnClick(object sender, RoutedEventArgs e)
    {
        FFmpegPathCard.Description = "No folder selected";
        await localSettings.SaveSettingAsync(SettingsViewModel.FFmpegPath, "");
        // Update ffmpeg runner
        await FFmpegRunner.Initialize();

        ShowInfoBar(InfoBarSeverity.Informational, "Reset to using built-in Ffmpeg");
    }

    private async void ResetYtdlpButton_OnClick(object sender, RoutedEventArgs e)
    {
        YtdlpPathCard.Description = "No file selected";
        await localSettings.SaveSettingAsync(SettingsViewModel.YtdlpPath, "");
        ShowInfoBar(InfoBarSeverity.Informational, "Reset to using bundled yt-dlp");
    }

    private async void UseYtdlpToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.UseYtdlp, UseYtdlpToggle.IsOn);
    }

    private async void AutoUpdateYtdlpToggle_Toggled(object sender, RoutedEventArgs e)
    {
        await localSettings.SaveSettingAsync(SettingsViewModel.AutoUpdateYtdlp, AutoUpdateYtdlpToggle.IsOn);
    }

    private async void ScrollViewerButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        var wildcard = button?.Content.ToString();

        var targetTextBox = button?.Tag as TextBox;

        if (targetTextBox == null) 
            return;

        if (!string.IsNullOrEmpty(wildcard))
        {
            targetTextBox.Text += wildcard;
            targetTextBox.Focus(FocusState.Programmatic); // Refocus the TextBox
            targetTextBox.SelectionStart = targetTextBox.Text.Length; // Move cursor to end of text
        }
    }
    
    private async void FileNameInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var button = sender as TextBox;
        var wildcard = button?.Text;
        await localSettings.SaveSettingAsync(SettingsViewModel.FileWildcard, wildcard ?? "");
    }

    private async void SubfolderNameInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var button = sender as TextBox;
        var wildcard = button?.Text;
        await localSettings.SaveSettingAsync(SettingsViewModel.SubfolderWildcard, wildcard ?? "");
    }

    private async void AlbumSubfolderNameInput_OnLostFocus(object sender, RoutedEventArgs e)
    {
        var button = sender as TextBox;
        var wildcard = button?.Text;
        await localSettings.SaveSettingAsync(SettingsViewModel.AlbumSubfolderWildcard, wildcard ?? "");
    }

    // Infobar helper methods ---------------------------------------------------
    private void PageInfoBar_OnCloseButtonClick(InfoBar sender, object args)
    {
        PageInfoBar.Opacity = 0;
    }

    private async void SubfoldersToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch)?.IsOn ?? false;
        SubfolderNameInput.IsEnabled = isToggled;
        SubfolderInfoBar.IsOpen = !isToggled;
        await localSettings.SaveSettingAsync(SettingsViewModel.Subfolders, isToggled);
    }

    private async void AlbumSubfoldersToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch)?.IsOn ?? false;
        AlbumSubfolderNameInput.IsEnabled = isToggled;
        AlbumSubfolderInfoBar.IsOpen = !isToggled;
        await localSettings.SaveSettingAsync(SettingsViewModel.AlbumSubfolders, isToggled);
        if (isToggled)
        {
            DownloadAlbumCoverArtToggle.IsOn = true;
            await localSettings.SaveSettingAsync(SettingsViewModel.DownloadAlbumCoverArt, true);
        }
    }

    private async void DownloadAlbumCoverArtToggle_OnToggled(object sender, RoutedEventArgs e)
    {
        var isToggled = (sender as ToggleSwitch)?.IsOn ?? false;
        await localSettings.SaveSettingAsync(SettingsViewModel.DownloadAlbumCoverArt, isToggled);
    }

    // Event handler to close the info bar and stop the timer (only ticks once)
    private void dispatcherTimer_Tick(object? sender, object e)
    {
        if (sender == null) return;
        PageInfoBar.Opacity = 0;
        (sender as DispatcherTimer).Stop();
        // Set IsOpen to false after 0.25 seconds
        Task.Factory.StartNew(() =>
        {
            System.Threading.Thread.Sleep(250);
            dispatcher.TryEnqueue(() =>
            {
                PageInfoBar.IsOpen = false;
            });
        });
    }

    private void ShowInfoBar(InfoBarSeverity severity, string message, int seconds = 2, string title = "", bool isMarkup = false)
    {
        title = StatusMarkup.Text(title.Trim());
        message = isMarkup ? message.Trim() : StatusMarkup.Text(message.Trim());
        dispatcher.TryEnqueue(() =>
        {
            dispatcherTimer.Stop();
            PageInfoBar.IsOpen = true;
            PageInfoBar.Opacity = 1;
            PageInfoBar.Severity = severity;
            //PageInfoBar.Title = title;

            if (!string.IsNullOrWhiteSpace(title))
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, $"<b>{title}</b>   {message}");
            }
            else
            {
                UrlParser.ParseTextBlock(InfoBarTextBlock, message);
            }
            dispatcherTimer.Interval = TimeSpan.FromSeconds(seconds);
            dispatcherTimer.Start();
        });
    }

    private async void UploadImportFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StorageFile? file = await StoragePickerHelper.PickFileAsync([".json"]);
            if (file != null)
            {
                string content = await FileIO.ReadTextAsync(file);
                // Check content length to avoid extremely long imports
                if (content.Length > 1E5)
                {
                    throw new Exception("Imported file too large. Double check imported file.");
                }
                else
                {
                    string? result = await localSettings.ImportSettingsAsync(content);
                    await SettingsViewModel.SetMissingDefaults();  // Fills in any null/improper values with defaults
                    await ViewModel.QueueDisplay.LoadAsync();
                    await App.GetService<FluentDL.Core.Services.LocalDisplaySettings>().LoadAsync();
                    await App.GetService<FluentDL.Core.Services.PreviewVolumeSettings>().LoadAsync();
                    await InitializeAsync();

                    if (string.IsNullOrEmpty(result))  // No error message
                    {
                        ShowInfoBar(InfoBarSeverity.Success, "You may need to restart the application for all changes to apply.", seconds: 3, title: "Import Successful");
                    } else
                    {
                        ShowInfoBar(InfoBarSeverity.Warning, result, seconds: 5, title: "Import Issue");
                    }
                }
            } else
            {
                ShowInfoBar(InfoBarSeverity.Informational, "Settings import cancelled.");
            }
        } catch (Exception ex)
        {
            ShowInfoBar(InfoBarSeverity.Error, ex.Message, seconds: 5, title: "Import Failed");
        }
    }

    private async void UploadExportFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StorageFile? file = await StoragePickerHelper.FileSavePickerAsync([".json"], "FluentDL_Settings");
            if (file != null)
            {
                string content = await localSettings.ExportSettingsAsync();
                await FileIO.WriteTextAsync(file, content);
                ShowInfoBar(InfoBarSeverity.Success, StatusMarkup.Format($"Settings saved to <a href='{file.Path}'>{file.Name}</a>."), seconds: 3, title: "Export Successful", isMarkup: true);
            }
            else
            {
                ShowInfoBar(InfoBarSeverity.Informational, "Settings export cancelled.");
            }
        }
        catch (Exception ex)
        {
            ShowInfoBar(InfoBarSeverity.Error, ex.Message, seconds: 5, title: "Export Failed");
        }
    }

    private async void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SettingsViewModel.ResetSettings();
            await ViewModel.QueueDisplay.LoadAsync();
            await App.GetService<FluentDL.Core.Services.LocalDisplaySettings>().LoadAsync();
            await App.GetService<FluentDL.Core.Services.PreviewVolumeSettings>().LoadAsync();
            await InitializeAsync();
            ShowInfoBar(InfoBarSeverity.Success, "You may need to restart the application for all changes to apply.", seconds: 3, title: "Reset Successful");
        }
        catch (Exception ex)
        {
            ShowInfoBar(InfoBarSeverity.Error, ex.Message, seconds: 5, title: "Reset Failed");
        }
    }

    private async void GetAppIdSecretButton_Click(object sender, RoutedEventArgs e)
    {
        QobuzApiService? defaultService = await Task.Run(() =>
        {
            try
            {
                return new QobuzApiService();
            } catch
            {
                return null;
            }
        });

        if (defaultService != null)
        {
            QobuzAppIdInput.Text = defaultService.AppId;
            QobuzAppSecretInput.Text = defaultService.AppSecret;
            ShowInfoBar(InfoBarSeverity.Informational, "Retrieved current player App ID and Secret. Press Save to apply.", seconds: 5);
        }
        else
        {
            ShowInfoBar(InfoBarSeverity.Error, "Failed to retrieve current player Qobuz App ID and Secret.", seconds: 5);
        }
    }
}
