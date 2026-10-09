using FluentDL.Core.Sources;
using FluentDL.Models;

namespace FluentDL.Contracts.Sources;

// The roles a music source can have. A source implements only the roles it supports, and callers find it through
// SourceRegistry. These live in the app project because SongSearchObject does; they move to Core with the models.
//
// Results go into a caller's collection as they arrive, the way the Search page shows them one by one.

public interface ISource
{
    SourceDescriptor Descriptor { get; }
}

/// <summary>Signing in with the credentials saved in settings.</summary>
public interface ISourceSession : ISource
{
    bool IsConnected { get; }

    /// <summary>Account details for the Settings page, or null when signed out.</summary>
    string? StatusText { get; }

    Task<ConnectResult> ConnectAsync();
}

public interface ISearchSource : ISource
{
    /// <summary>Adds one page of results. In album mode, each result is an album.</summary>
    Task SearchAsync(ICollection<SongSearchObject> results, string query, PageRequest page, bool albumMode, CancellationToken token);

    Task AdvancedSearchAsync(ICollection<SongSearchObject> results, AdvancedQuery query, PageRequest page, CancellationToken token);
}

public interface ILinkSource : ISource
{
    bool CanHandle(string url);

    /// <summary>
    /// Adds the tracks (or albums, in album mode) behind a link. Most links load everything on the first page and
    /// nothing after it; Qobuz label and artist links load one page at a time.
    /// </summary>
    Task AddFromLinkAsync(ICollection<SongSearchObject> results, string url, LinkRequest request, UrlStatusUpdateCallback? status, CancellationToken token);
}

/// <summary>Finding a track on this source that matches a track from another source.</summary>
public interface IConversionTarget : ISource
{
    Task<MatchResult> FindEquivalentAsync(SongSearchObject song, CancellationToken token);
}

/// <summary>Looking up full items by id.</summary>
public interface ICatalogSource : ISource
{
    /// <summary>The full tracks of an album search result, as the queue needs them. Tracks that fail to load are left out.</summary>
    Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album);
}

public sealed record PageRequest(int Offset, int Limit);

public sealed record AdvancedQuery(string Artist, string Track, string Album);

public sealed record LinkRequest(bool IsFirstPage, PageRequest Page, bool AlbumMode);

/// <summary>
/// The outcome of signing in. <paramref name="Message"/> is null when the source reported nothing, such as Deezer
/// with no ARL saved.
/// </summary>
public sealed record ConnectResult(bool Connected, StatusLevel Level, string? Message);

/// <summary>The match found, if any, and the badge the queue shows for it.</summary>
public sealed record MatchResult(SongSearchObject? Track, MatchKind Kind);

public enum MatchKind
{
    /// <summary>Same recording, usually found by ISRC. Green badge.</summary>
    Exact,

    /// <summary>A likely equivalent found by title, artist and album. Yellow badge.</summary>
    Close,

    /// <summary>No match. Red badge.</summary>
    NotFound,

    /// <summary>The source returned without reporting, for example when the conversion was cancelled.</summary>
    NotReported,
}
