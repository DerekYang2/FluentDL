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

/// <summary>Signing in to the user's own account in the browser, once; the sign-in is saved (TIDAL).</summary>
public interface IAccountLogin : ISource
{
    /// <summary>Whether this build can sign in at all.</summary>
    bool CanLogin { get; }

    /// <summary>The account's name while signed in to it, else null.</summary>
    string? AccountName { get; }

    /// <summary>Opens the sign-in page and waits for the user, up to a few minutes or until cancelled.</summary>
    Task<ConnectResult> LoginAsync(CancellationToken token);

    Task LogoutAsync();
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
    /// <param name="isrc">The song's ISRC, looked up by ConversionService when the song has none.</param>
    /// <param name="isrcOnly">Match by ISRC only. Deezer and Qobuz support it; the others ignore it.</param>
    Task<MatchResult> FindEquivalentAsync(SongSearchObject song, string? isrc, CancellationToken token, bool isrcOnly = false);
}

/// <summary>Downloading a track's audio from this source.</summary>
public interface IDownloadSource : ISource
{
    /// <summary>Downloads to <paramref name="pathWithoutExtension"/> plus the extension of the format served, and returns the full path.</summary>
    Task<string> DownloadAsync(SongSearchObject track, string pathWithoutExtension, DownloadRequest request, IProgress<ProgressData>? progress);
}

/// <param name="Quality">Which quality to ask the source for.</param>
/// <param name="Allow128Fallback">Deezer: when the requested quality fails, retry at 128 kbps (and convert to FLAC if FLAC
/// was asked and FFmpeg is set up). With <see cref="DownloadQuality.Mp3"/>, Deezer then asks for 128 kbps directly, as
/// the old Spotify equivalent chain did.</param>
public sealed record DownloadRequest(DownloadQuality Quality = DownloadQuality.Setting, bool Allow128Fallback = false);

public enum DownloadQuality
{
    /// <summary>The source's own quality setting.</summary>
    Setting,

    /// <summary>MP3 (Deezer 320 kbps, Qobuz format 5).</summary>
    Mp3,

    /// <summary>Lossless (Deezer FLAC, Qobuz 16-bit FLAC).</summary>
    Lossless,

    /// <summary>Whatever the source serves, with no conversion (YouTube's opus stream).</summary>
    AsServed,
}

/// <summary>Looking up the ISRC of this source's own track, for songs that don't carry one (Spotify web player results).</summary>
public interface IIsrcSource : ISource
{
    Task<string?> GetIsrcAsync(string trackId);
}

/// <summary>Looking up full items by id.</summary>
public interface ICatalogSource : ISource
{
    Task<SongSearchObject?> GetTrackAsync(string id);

    /// <summary>
    /// The full tracks of an album search result (search results only carry part of each track). The queue and album
    /// downloads both use it. Tracks that fail to load are left out.
    /// </summary>
    Task<List<SongSearchObject>> GetAlbumTracksAsync(AlbumSearchObject album);

    /// <summary>The largest cover image this source has for the album, or null.</summary>
    Task<string?> GetLargestCoverUrlAsync(AlbumSearchObject album);
}

/// <summary>Writing this source's metadata (tags, cover) into a downloaded file.</summary>
public interface ITagSource : ISource
{
    Task WriteTagsAsync(string filePath, SongSearchObject track);
}

/// <summary>What the Search page's preview pane shows for a selected track or album.</summary>
public interface IPreviewSource : ISource
{
    Task<PreviewInfo> GetPreviewAsync(SongSearchObject song);

    /// <summary>The preview audio's URL, for a track whose PreviewInfo loads it separately or a track in an album's list.</summary>
    Task<string?> GetPreviewAudioAsync(SongSearchObject track);
}

/// <summary>
/// The pane always shows Artists, Release Date, Length and Album rows from the item itself; this is what a source
/// adds or changes. Each source fills it the way its old branch in PreviewPane did.
/// </summary>
public sealed class PreviewInfo
{
    /// <summary>Null shows no cover. An empty string fails, as it did before.</summary>
    public string? CoverUrl { get; init; }

    /// <summary>Replaces the item's release date in the Release Date row.</summary>
    public string? ReleaseDate { get; init; }

    public bool RemoveAlbumRow { get; init; }

    /// <summary>Rows added after the common ones, in order.</summary>
    public List<(string Label, string Value)> Rows { get; init; } = [];

    /// <summary>0 to 1. When set, the pane adds an empty Popularity row with the rating control.</summary>
    public double? Popularity { get; init; }

    /// <summary>For albums: the track list and the count shown above it.</summary>
    public List<SongSearchObject>? AlbumTracks { get; init; }

    public int AlbumTrackCount { get; init; }

    /// <summary>For tracks: the preview audio, when the details already include it.</summary>
    public string? AudioUrl { get; init; }

    /// <summary>For tracks: show the details first, then call GetPreviewAudioAsync.</summary>
    public bool LoadAudioSeparately { get; init; }
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

/// <summary>Finding an album by its UPC (barcode). Album conversion tries this before an album search.</summary>
public interface IAlbumUpcSource : ISource
{
    Task<AlbumSearchObject?> GetAlbumByUpcAsync(string upc, CancellationToken token);
}

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
