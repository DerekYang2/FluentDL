using System.Net;
using System.Text.Json;
using FluentDL.Services.Tidal;
using FluentDL.Services.Playlists;
using FluentDL.Services.Sources;
using FluentDL.Core.Contracts.Services;
using FluentDL.Core.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace FluentDL;

// Only the explicit preview build includes this file. It never forwards requests to a network handler.
internal static class PlaylistDialogTestSupport
{
    internal const string PlaylistId = "11111111-2222-3333-4444-555555555555";

    internal static void Configure(Views.CreatePlaylistDialog dialog)
    {
        RecordPickerLayout(dialog);
        dialog.OpenPlaylistAsync = uri =>
        {
            var id = dialog.ViewModel.SelectedPlaylist.Playlist?.Id ?? "123456789";
            var expected = dialog.ViewModel.SelectedDestination?.Writer.Descriptor.Id switch
            {
                "deezer" => "https://www.deezer.com/playlist/" + id,
                "qobuz" => "https://play.qobuz.com/playlist/" + id,
                _ => "https://tidal.com/browse/playlist/" + (dialog.ViewModel.SelectedPlaylist.Playlist?.Id ?? PlaylistId),
            };
            if (uri.AbsoluteUri != expected)
                throw new InvalidOperationException("Unexpected preview navigation target.");
            var folder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "PlaylistUiPreview");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "opened-uri.txt"), uri.AbsoluteUri);
            return Task.FromResult(true);
        };
    }

    private static void RecordPickerLayout(Views.CreatePlaylistDialog dialog)
    {
        var picker = (Microsoft.UI.Xaml.Controls.ComboBox)dialog.FindName("PlaylistTargetInput");
        var refresh = (Microsoft.UI.Xaml.Controls.Button)dialog.FindName("RefreshPlaylistsButton");
        var content = (Microsoft.UI.Xaml.FrameworkElement)dialog.FindName("PlaylistContent");
        var fields = new[] { "PlaylistSourceInput", "PlaylistNameInput", "PlaylistDescriptionInput", "PlaylistVisibilityInput" }
            .Select(name => (Microsoft.UI.Xaml.FrameworkElement)dialog.FindName(name)).ToArray();
        string? previous = null;
        void Record()
        {
            if (!dialog.IsLoaded || dialog.ViewModel.HasResult || picker.ActualHeight <= 0 || refresh.ActualHeight <= 0) return;
            var pickerTop = picker.TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point()).Y;
            var refreshTop = refresh.TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point()).Y;
            var contentLeft = content.TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point()).X;
            var json = JsonSerializer.Serialize(new
            {
                pickerHeight = picker.ActualHeight, refreshHeight = refresh.ActualHeight, pickerTop, refreshTop,
                contentLeft, contentWidth = content.ActualWidth,
                fields = fields.Select(field => new
                {
                    name = field.Name, width = field.ActualWidth,
                    left = field.TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point()).X,
                }).ToArray(),
            });
            if (json == previous) return;
            previous = json;
            var folder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "PlaylistUiPreview");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "picker-layout.json"), json);
        }
        picker.SizeChanged += (_, _) => Record();
        refresh.SizeChanged += (_, _) => Record();
        foreach (var field in fields) field.SizeChanged += (_, _) => dialog.DispatcherQueue.TryEnqueue(Record);
        content.SizeChanged += (_, _) => dialog.DispatcherQueue.TryEnqueue(Record);
        dialog.Opened += (_, _) => dialog.DispatcherQueue.TryEnqueue(Record);
    }

    private static bool SignedIn => Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("PlaylistUiPreviewSignedIn", out var value) && value?.ToString() == "true";

    internal static void Register(IServiceCollection services)
    {
        services.AddSingleton(provider => new TidalPlaylistClient(provider.GetRequiredService<TidalDownloadClient>(), new HttpClient(new PreviewHttp())));
        var identity = new object();
        services.AddSingleton(provider => new DeezerSource(provider.GetRequiredService<ILocalSettingsService>(), provider.GetRequiredService<IAudioConverter>(),
            new DeezerPlaylistClient(() => SignedIn ? new("ui-test-arl", "ui-test-sid", "ui-test-form", identity, "7") : null, new HttpClient(new ServicePreviewHttp("deezer")))));
        services.AddSingleton(provider => new QobuzSource(provider.GetRequiredService<ILocalSettingsService>(),
            new QobuzPlaylistClient(() => SignedIn ? new("ui-test-app", "ui-test-user", identity, "7") : null, new HttpClient(new ServicePreviewHttp("qobuz")))));
    }

    private sealed class ServicePreviewHttp(string source) : HttpMessageHandler
    {
        private readonly List<string> createdTracks = [];
        private readonly List<string> existingTracks = [source == "deezer" ? "3135556" : "9140031"];
        private List<string> tracks = [];
        private bool partial;
        private int visibility;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object reply;
            if (source == "deezer")
            {
                if (request.Headers.GetValues("Cookie").Single() != "arl=ui-test-arl; sid=ui-test-sid")
                    throw new InvalidOperationException("Only synthetic Deezer credentials are allowed.");
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var body = document.RootElement;
                var method = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["method"];
                tracks = body.TryGetProperty("PLAYLIST_ID", out var requestedId) && requestedId.ToString() == "987654321" ? existingTracks : createdTracks;
                if (ReferenceEquals(tracks, existingTracks)) partial = false;
                if (method == "playlist.create")
                {
                    tracks.Clear();
                    partial = body.GetProperty("title").GetString()!.Contains("Partial", StringComparison.Ordinal);
                    visibility = body.GetProperty("status").GetInt32();
                    reply = 123456789;
                }
                else if (method == "deezer.pageProfile") reply = new { TAB = new { playlists = new { data = new[] { new { PLAYLIST_ID = 987654321, TITLE = "Existing Deezer playlist", PARENT_USER_ID = "7" } }, total = 1 } } };
                else if (method == "deezer.pagePlaylist") reply = new { DATA = new { STATUS = visibility, PARENT_USER_ID = "7" } };
                else if (method == "playlist.addSongs")
                {
                    var ids = body.GetProperty("songs").EnumerateArray().Select(pair => pair[0].GetString()!).ToArray();
                    tracks.AddRange(partial ? ids.Take(1) : ids);
                    Record();
                    reply = true;
                }
                else if (method == "playlist.getSongs") reply = new { data = tracks.Select(id => new { SNG_ID = id }).ToArray(), total = tracks.Count };
                else throw new InvalidOperationException("Unsupported synthetic Deezer method.");
                reply = new { error = new { }, results = reply };
            }
            else
            {
                if (request.Headers.GetValues("X-User-Auth-Token").Single() != "ui-test-user")
                    throw new InvalidOperationException("Only synthetic Qobuz credentials are allowed.");
                var body = request.Content == null ? null : System.Web.HttpUtility.ParseQueryString(await request.Content.ReadAsStringAsync(token));
                var method = request.RequestUri!.AbsolutePath.Split('/').Last();
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                tracks = (body?["playlist_id"] ?? query["playlist_id"]) == "987654321" ? existingTracks : createdTracks;
                if (ReferenceEquals(tracks, existingTracks)) partial = false;
                if (method == "create")
                {
                    tracks.Clear();
                    partial = body!["name"]!.Contains("Partial", StringComparison.Ordinal);
                    visibility = int.Parse(body["is_public"]!);
                    reply = new { id = 123456789, is_public = visibility == 1 };
                }
                else if (method == "addTracks")
                {
                    var ids = body!["track_ids"]!.Split(',');
                    tracks.AddRange(partial ? ids.Take(1) : ids);
                    Record();
                    reply = new { id = 123456789, tracks_count = tracks.Count };
                }
                else if (method == "getUserPlaylists") reply = new { playlists = new { items = new[] { new { id = 987654321, name = "Existing Qobuz playlist", owner = new { id = "7" } } }, total = 1 } };
                else if (method == "get") reply = new { track_ids = tracks.Select(long.Parse).ToArray(), tracks_count = tracks.Count, owner = new { id = "7" } };
                else throw new InvalidOperationException("Unsupported synthetic Qobuz method.");
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(reply)) };
        }
        private void Record()
        {
            var folder = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "PlaylistUiPreview");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "uploaded-" + source + ".json"), JsonSerializer.Serialize(tracks));
        }
    }

    private sealed class PreviewHttp : HttpMessageHandler
    {
        private bool partial;
        private readonly List<string> tracks = [];
        private readonly List<string> existing = ["20115564"];
        private const string ExistingId = "99999999-2222-3333-4444-555555555555";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Headers.Authorization?.Parameter != "ui-test-token")
                throw new InvalidOperationException("Playlist preview requires the synthetic Sandbox account, not real credentials.");
            var active = request.RequestUri!.AbsolutePath.Contains(ExistingId, StringComparison.Ordinal) ? existing : tracks;
            if (request.Method == HttpMethod.Get)
            {
                object metadata = new { id = ExistingId, attributes = new { name = "Existing TIDAL playlist" }, relationships = new { owners = new { data = new[] { new { id = "7", type = "users" } } } } };
                object data = request.RequestUri.AbsolutePath.EndsWith("/items") ? active.Select(id => new { id, type = "tracks" }).ToArray()
                    : request.RequestUri.AbsolutePath.EndsWith("/playlists") ? new[] { metadata } : metadata;
                return Json(HttpStatusCode.OK, new { data });
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (request.RequestUri!.AbsolutePath.EndsWith("/playlists"))
            {
                partial = body.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("name").GetString()!.Contains("Partial", StringComparison.Ordinal);
                tracks.Clear();
                return Json(HttpStatusCode.Created, new { data = new { id = PlaylistId, type = "playlists" } });
            }
            var items = body.RootElement.GetProperty("data").EnumerateArray().Select(item => new { id = item.GetProperty("id").GetString(), type = "tracks" }).ToArray();
            active.AddRange((partial && ReferenceEquals(active, tracks) ? items.Take(1) : items).Select(item => item.id!));
            return Json(HttpStatusCode.OK, new { data = partial ? items.Take(1).ToArray() : items });
        }

        private static HttpResponseMessage Json(HttpStatusCode code, object body) => new(code) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
