namespace FluentDL.Core.Sources;

/// <summary>
/// The source ids stored in SongSearchObject.Source, the queue database (row key = Source + Id), queue backups and
/// the search_source setting. They are persisted, so the values must never change (see ADR-001).
/// </summary>
public static class SourceIds
{
    public const string Deezer = "deezer";
    public const string Qobuz = "qobuz";
    public const string Spotify = "spotify";
    public const string YouTube = "youtube";
    public const string Tidal = "tidal";
    public const string Local = "local";
}
