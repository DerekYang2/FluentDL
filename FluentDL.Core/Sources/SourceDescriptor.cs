#nullable enable
namespace FluentDL.Core.Sources;

/// <summary>
/// What the UI needs to know about one source: its name, color, search page sizes and link format.
/// </summary>
/// <param name="Id">Persisted id, one of <see cref="SourceIds"/>.</param>
/// <param name="DisplayName">Name shown in pickers and dialogs, such as "YouTube".</param>
/// <param name="R">Red part of the accent color used for the source dot and album icon.</param>
/// <param name="G">Green part of the accent color.</param>
/// <param name="B">Blue part of the accent color.</param>
/// <param name="IsSearchable">Shown in the Search page's source picker.</param>
/// <param name="InitialChunkSize">Results loaded by a new search.</param>
/// <param name="SearchChunkSize">Results loaded each time the list scrolls to the end.</param>
/// <param name="TrackUrlPrefix">Prepended to a track id to make its link. Empty when the id is already the link (local files).</param>
/// <param name="AlbumUrlPrefix">Prepended to an album id to make its link.</param>
public sealed record SourceDescriptor(
    string Id,
    string DisplayName,
    byte R,
    byte G,
    byte B,
    bool IsSearchable,
    int InitialChunkSize,
    int SearchChunkSize,
    string TrackUrlPrefix,
    string AlbumUrlPrefix)
{
    public string TrackUrl(string id) => TrackUrlPrefix + id;

    public string AlbumUrl(string id) => AlbumUrlPrefix + id;
}
