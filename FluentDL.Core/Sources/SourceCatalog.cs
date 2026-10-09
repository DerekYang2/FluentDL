#nullable enable
namespace FluentDL.Core.Sources;

/// <summary>
/// Every source the app knows, in the order the UI lists them. Adding a source here adds it to the Search page's
/// source picker (if searchable) and to the queue's clear and conversion dialogs.
/// </summary>
public static class SourceCatalog
{
    public static IReadOnlyList<SourceDescriptor> All { get; } =
    [
        new(SourceIds.Deezer, "Deezer", 160, 55, 250, IsSearchable: true, InitialChunkSize: 20, SearchChunkSize: 10,
            "https://www.deezer.com/track/", "https://www.deezer.com/album/"),
        new(SourceIds.Qobuz, "Qobuz", 0, 0, 0, IsSearchable: true, InitialChunkSize: 20, SearchChunkSize: 10,
            "https://open.qobuz.com/track/", "https://open.qobuz.com/album/"),
        new(SourceIds.Spotify, "Spotify", 15, 213, 101, IsSearchable: true, InitialChunkSize: 20, SearchChunkSize: 10,
            "https://open.spotify.com/track/", "https://open.spotify.com/album/"),
        new(SourceIds.YouTube, "YouTube", 255, 0, 0, IsSearchable: true, InitialChunkSize: 10, SearchChunkSize: 5,
            "https://www.youtube.com/watch?v=", "https://music.youtube.com/playlist?list="),
        // Local files: the id is the file path, which is also the "link".
        new(SourceIds.Local, "Local", 255, 255, 255, IsSearchable: false, InitialChunkSize: 0, SearchChunkSize: 0,
            "", ""),
    ];

    public static IReadOnlyList<SourceDescriptor> Searchable { get; } = All.Where(s => s.IsSearchable).ToArray();

    /// <summary>The descriptor for a persisted id, or null for an unknown id.</summary>
    public static SourceDescriptor? Find(string? id) => All.FirstOrDefault(s => s.Id == id);
}
