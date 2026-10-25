using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentDL.Models;
using FluentDL.Services.Playlists;

namespace FluentDL.Services.Sources;

internal sealed partial class DeezerSource
{
    private IAsyncEnumerable<SongSearchObject?> LoadPlaylistMetadataAsync(DeezerPlaylistContents contents, CancellationToken token) =>
        LoadTrackMetadataAsync(contents.TrackIds, contents.SessionIdentity, token);

    private async IAsyncEnumerable<SongSearchObject?> LoadTrackMetadataAsync(IReadOnlyList<string> trackIds, object? sessionIdentity,
        [EnumeratorCancellation] CancellationToken token)
    {
        var metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var useBatch = sessionIdentity != null;
        void CheckSession()
        {
            token.ThrowIfCancellationRequested();
            if (sessionIdentity != null) playlists.RequireReadSession(sessionIdentity);
        }
        foreach (var page in trackIds.Chunk(DeezerPlaylistClient.MetadataBatchSize))
        {
            CheckSession();
            var needed = page.Distinct().Where(id => !metadata.ContainsKey(id)).ToArray();
            if (useBatch && needed.Length > 0 && needed.All(id => long.TryParse(id, out var number) && number > 0 && id.All(char.IsAsciiDigit)))
            {
                try
                {
                    foreach (var record in await playlists.ReadTrackBatchAsync(needed, sessionIdentity!, token))
                    {
                        if (DeezerGatewayTrackMapper.TryConvert(record, out var mapped))
                        {
                            var id = mapped.GetProperty("id").GetString()!;
                            if (needed.Contains(id)) metadata.TryAdd(id, mapped);
                        }
                    }
                }
                catch (Exception error) when (error is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    // Keep the individual-track path if this session cannot use metadata batches.
                    useBatch = false;
                }
            }

            foreach (var id in page)
            {
                CheckSession();
                if (!metadata.TryGetValue(id, out var record))
                {
                    record = await restClient.FetchJsonElement("track/" + id, token);
                    CheckSession();
                    metadata[id] = record;
                }
                var track = GetTrackFromJsonElement(record);
                if (track != null)
                    track.Artists = string.Join(", ", track.Artists.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct());
                // Map each occurrence separately so duplicate entries do not share mutable model state.
                yield return track?.Id == id ? track : null;
            }
        }
    }
}
