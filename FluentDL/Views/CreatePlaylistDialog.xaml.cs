using FluentDL.ViewModels;
using FluentDL.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentDL.Views;

public sealed partial class CreatePlaylistDialog : ContentDialog
{
    public PlaylistDialogViewModel ViewModel { get; }
    private bool openingPlaylist;
    private XamlRoot? layoutRoot;
    internal Func<Uri, Task<bool>> OpenPlaylistAsync { get; set; } = async uri => await Windows.System.Launcher.LaunchUriAsync(uri);

    public CreatePlaylistDialog(PlaylistDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        UpdateStatusText();
#if FLUENTDL_PLAYLIST_UI_TESTS
        PlaylistDialogTestSupport.Configure(this);
#endif
    }

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility FormVisibility(bool hasResult) => hasResult ? Visibility.Collapsed : Visibility.Visible;
    public static InfoBarSeverity NoticeSeverity(PlaylistNoticeLevel value) => value switch
    {
        PlaylistNoticeLevel.Success => InfoBarSeverity.Success,
        PlaylistNoticeLevel.Warning => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Informational,
    };

    private async void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        layoutRoot = XamlRoot;
        layoutRoot.Changed += LayoutRoot_Changed;
        UpdateFormWidth();
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateStatusText();
        await ViewModel.CheckSignInAsync();
    }

    private void LayoutRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateFormWidth();

    private void UpdateFormWidth()
    {
        // Give the form a finite measure width, including when its longest dropdown item is selected.
        // Reserve room for the dialog's content padding and outer margins on a narrow window.
        if (layoutRoot != null) PlaylistContent.Width = Math.Max(0, Math.Min(440, layoutRoot.Size.Width - 96));
    }
    private async void RefreshPlaylists_Click(object sender, RoutedEventArgs args) => await ViewModel.CheckSignInAsync();
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ViewModel.Message) or nameof(ViewModel.SignInSourceId) or nameof(ViewModel.NeedsSignIn))
            UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        var destination = SettingsNavigation.ForSource(ViewModel.SignInSourceId ?? ViewModel.SelectedDestination?.Writer.Descriptor.Id);
        var message = destination is { } target && (ViewModel.NeedsSignIn || ViewModel.Message.Contains(SettingsNavigation.Path(target), StringComparison.Ordinal))
            ? SettingsNavigation.WithLink(ViewModel.Message, target) : FluentDL.Core.Sources.StatusMarkup.Text(ViewModel.Message);
        UrlParser.ParseTextBlock(PlaylistStatusText, message, section =>
        {
            ViewModel.Cancel();
            Hide();
            SettingsNavigation.Open(section);
        });
    }
    private async void OpenSelectedPlaylist_Click(object sender, RoutedEventArgs args)
    {
        if (ViewModel.CanOpenSelectedPlaylist && ViewModel.SelectedPlaylist.Playlist is { } playlist)
            await OpenPlaylistUriAsync(playlist.Url);
    }

    private async void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // Keep results, partial failures and the playlist link in the dialog.
        if (ViewModel.HasResult)
        {
            if (ViewModel.PlaylistUrl is { } url) await OpenPlaylistUriAsync(url);
            return;
        }
        await ViewModel.CreateAsync();
        if (ViewModel.CanUsePrimary && GetTemplateChild("PrimaryButton") is Button primary)
            primary.Focus(FocusState.Programmatic);
    }

    private async Task OpenPlaylistUriAsync(Uri url)
    {
        if (openingPlaylist) return;
        openingPlaylist = true;
        try
        {
            if (!await OpenPlaylistAsync(url)) ViewModel.ReportOpenFailure();
        }
        catch (Exception error)
        {
            Serilog.Log.Warning("Could not open playlist: {ExceptionType}", error.GetType().Name);
            ViewModel.ReportOpenFailure();
        }
        finally { openingPlaylist = false; }
    }

    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!ViewModel.IsBusy) return;
        args.Cancel = true;
        ViewModel.Cancel(); // Keep the dialog until the writer reports its final/uncertain result.
    }

    private void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        if (layoutRoot != null) layoutRoot.Changed -= LayoutRoot_Changed;
        layoutRoot = null;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Cancel();
    }

}
