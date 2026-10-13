using FluentDL.Services.Tidal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace FluentDL.Views;

public sealed partial class TidalDownloadLoginDialog : ContentDialog
{
    private readonly TidalDownloadClient client;
    private readonly TidalDownloadClient.Login login;
    private readonly CancellationTokenSource closing = new();
    private bool completing;
    public bool SignedIn { get; private set; }

    public TidalDownloadLoginDialog(TidalDownloadClient client, TidalDownloadClient.Login login)
    {
        InitializeComponent();
        this.client = client;
        this.login = login;
    }

    private async void Dialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoginWebView.EnsureCoreWebView2Async();
            if (closing.IsCancellationRequested) return;
            LoginWebView.CoreWebView2.NavigationStarting += NavigationStarting;
            LoginWebView.Source = login.Address;
        }
        catch (Exception)
        {
            if (closing.IsCancellationRequested) return;
            LoginError.Message = "The embedded browser couldn't start. Use Open in my browser instead.";
            LoginError.IsOpen = true;
            PastePanel.Visibility = Visibility.Visible;
            LoginWebView.Visibility = Visibility.Collapsed;
        }
    }

    private async void NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || !TidalDownloadClient.IsCallback(uri)) return;
        args.Cancel = true;
        await CompleteAsync(args.Uri);
    }

    private async void Browser_Click(object sender, RoutedEventArgs e)
    {
        PastePanel.Visibility = Visibility.Visible;
        LoginWebView.Visibility = Visibility.Collapsed;
        LoginWebView.CoreWebView2?.Stop();
        try
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(login.Address))
                throw new InvalidOperationException("Couldn't open the browser.");
        }
        catch (Exception)
        {
            LoginError.Message = "Couldn't open your default browser. Check your default browser setting and try again.";
            LoginError.IsOpen = true;
        }
    }

    private async void Finish_Click(object sender, RoutedEventArgs e) => await CompleteAsync(RedirectInput.Text);

    private async Task CompleteAsync(string address)
    {
        if (completing || closing.IsCancellationRequested) return;
        completing = true;
        FinishButton.IsEnabled = BrowserButton.IsEnabled = false;
        try
        {
            await client.FinishLoginAsync(login, address, closing.Token);
            SignedIn = true;
            Hide();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!closing.IsCancellationRequested)
            {
                LoginError.Message = e is HttpRequestException or InvalidOperationException ? e.Message : "Couldn't save the sign-in. Close this dialog and try again.";
                LoginError.IsOpen = true;
            }
        }
        finally
        {
            completing = false;
            FinishButton.IsEnabled = BrowserButton.IsEnabled = true;
        }
    }

    private void Dialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        closing.Cancel();
        RedirectInput.Text = "";
        if (LoginWebView.CoreWebView2 != null) LoginWebView.CoreWebView2.NavigationStarting -= NavigationStarting;
        LoginWebView.Close();
        closing.Dispose();
    }
}
