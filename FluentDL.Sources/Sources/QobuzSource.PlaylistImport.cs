using System.Net;
using System.Globalization;
using FluentDL.Core.Sources;
using FluentDL.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QobuzApiSharp.Models.Content;

namespace FluentDL.Services.Sources;

internal sealed partial class QobuzSource
{
    private async Task ImportPlaylistAsync(ICollection<SongSearchObject> results, string url, string id,
        UrlStatusUpdateCallback? status, CancellationToken token)
    {
        const int pageSize = 50;
        var client = QobuzHttpClient;
        var loaded = 0;
        var failed = 0;
        var offset = 0;
        var name = id;
        var details = new Dictionary<int, Track?>();
        void CheckSession()
        {
            token.ThrowIfCancellationRequested();
            if (!HttpClientInitialized || !ReferenceEquals(client, QobuzHttpClient))
                throw new InvalidOperationException("The Qobuz catalog session changed. Load the playlist again.");
        }
        async Task<JObject> Read(string endpoint)
        {
            CheckSession();
            using var response = await CatalogRequests.SendAsync(client, () => new(HttpMethod.Get, BaseUrl + "/" + endpoint), token);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(token);
            CheckSession();
            var root = await Task.Run(() => JObject.Parse(json), token);
            CheckSession();
            if (root.Value<string>("status") == "error") throw new JsonException("Qobuz refused the playlist request.");
            return root;
        }
        async Task Add(JObject item)
        {
            CheckSession();
            if (!int.TryParse(item["id"]?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var trackId) || trackId <= 0)
            { failed++; return; }
            if (!details.TryGetValue(trackId, out var track))
            {
                if (CompletePlaylistTrack(item)) track = item.ToObject<Track>();
                else
                {
                    try
                    {
                        var detail = await Read("track/get?track_id=" + trackId.ToString(CultureInfo.InvariantCulture));
                        track = CompletePlaylistTrack(detail) ? detail.ToObject<Track>() : null;
                    }
                    catch (Exception error) when (error is HttpRequestException or JsonException) { track = null; }
                }
                details[trackId] = track;
            }
            if (track?.Id != trackId) { failed++; return; }
            CheckSession();
            results.Add(ConvertSongSearchObject(track));
            loaded++;
        }
        try
        {
            status?.Invoke(StatusLevel.Informational, StatusMarkup.Format($"<b>Qobuz</b>   Loading playlist <a href='{url}'>{name}</a>"), -1);
            JObject? first;
            try { first = await Read($"playlist/get?playlist_id={Uri.EscapeDataString(id)}&extra=tracks&offset=0&limit={pageSize}"); }
            catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.BadRequest) { first = null; }
            if (first?["tracks"]?["items"] is not JArray)
            {
                // Retain ID-only import for a server that does not expose playlist track records.
                var legacy = first?["track_ids"] != null ? first : await Read("playlist/get?playlist_id=" + Uri.EscapeDataString(id) + "&extra=track_ids");
                name = legacy?.Value<string>("name") ?? name;
                var ids = legacy?["track_ids"];
                var ordered = ids switch
                {
                    JArray array => array.AsEnumerable(),
                    JObject map => map.Properties().OrderBy(property => int.Parse(property.Name, CultureInfo.InvariantCulture)).Select(property => property.Value),
                    _ => throw new JsonException("Qobuz returned no playlist tracks."),
                };
                foreach (var trackId in ordered)
                {
                    await Add(new JObject { ["id"] = trackId });
                    await Task.Delay(1, token);
                }
            }
            else
            {
                var page = first;
                while (true)
                {
                    CheckSession();
                    name = page.Value<string>("name") ?? name;
                    var container = page["tracks"] as JObject ?? throw new JsonException("Qobuz returned no playlist track page.");
                    var items = container["items"] as JArray ?? throw new JsonException("Qobuz returned no playlist track page.");
                    if (container.Value<int?>("offset") is { } actualOffset && actualOffset != offset)
                        throw new JsonException("Qobuz returned an unexpected playlist page.");
                    foreach (var item in items)
                    {
                        if (item is JObject record) await Add(record);
                        else failed++;
                    }
                    offset += items.Count;
                    var total = container.Value<int?>("total") ?? page.Value<int?>("tracks_count")
                        ?? throw new JsonException("Qobuz did not report the playlist size.");
                    if (total < 0) throw new JsonException("Qobuz reported an invalid playlist size.");
                    status?.Invoke(StatusLevel.Informational, StatusMarkup.Format($"<b>Qobuz</b>   Loading playlist <a href='{url}'>{name}</a>... {loaded:N0} tracks"), -1);
                    await Task.Delay(1, token);
                    if (offset >= total) break;
                    if (items.Count == 0) throw new JsonException("Qobuz returned an incomplete playlist page.");
                    page = await Read($"playlist/get?playlist_id={Uri.EscapeDataString(id)}&extra=tracks&offset={offset}&limit={pageSize}");
                }
            }
            status?.Invoke(failed == 0 ? StatusLevel.Success : StatusLevel.Warning,
                StatusMarkup.Format($"<b>Qobuz</b>   Loaded playlist <a href='{url}'>{name}</a>")
                + (failed == 0 ? "" : $". {failed:N0} tracks could not be loaded."));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            status?.Invoke(loaded > 0 ? StatusLevel.Warning : StatusLevel.Error,
                loaded > 0 ? $"<b>Qobuz</b>   Loaded {loaded:N0} tracks, but could not finish the playlist. Results are incomplete. Try loading the link again."
                    : "Qobuz could not load this playlist. Check the link and your account access.");
        }
    }

    internal static bool CompletePlaylistTrack(JObject item)
    {
        try
        {
            var track = item.ToObject<Track>();
            return track?.Id is > 0 && !string.IsNullOrWhiteSpace(track.Title) && !string.IsNullOrWhiteSpace(track.Isrc)
                && track.Duration is > 0 && track.TrackNumber is > 0 && track.ParentalWarning.HasValue
                && track.ReleaseDateOriginal.HasValue && track.ReleaseDateOriginal.Value.Year > 1
                && !string.IsNullOrWhiteSpace(track.Performer?.Name) && !string.IsNullOrWhiteSpace(track.Performers)
                && !string.IsNullOrWhiteSpace(track.Album?.Title) && !string.IsNullOrWhiteSpace(track.Album?.Image?.Small);
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidCastException or OverflowException) { return false; }
    }
}
