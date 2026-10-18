namespace FluentDL.Contracts.Sources;

/// <summary>Requests user consent from an explicit Settings sign-in action. Readiness checks never prompt.</summary>
public interface IPlaylistAuthorization : ISource
{
    Task<bool> AuthorizePlaylistAsync(CancellationToken token);
}

/// <summary>Creates or appends to an owned playlist using this source's track IDs. Never matches or converts tracks.</summary>
public interface IPlaylistWriter : ISource
{
    IReadOnlyList<PlaylistVisibility> SupportedPlaylistVisibilities { get; }
    string PlaylistSignInHint { get; }
    int PlaylistNameMaxLength { get; }
    string NormalizePlaylistTrackId(string id) => long.TryParse(id, out var number)
        ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : id;
    Task<bool> CanCreatePlaylistAsync(CancellationToken token);
    Task<IReadOnlyList<OwnedPlaylist>> GetOwnedPlaylistsAsync(CancellationToken token);
    Task<IReadOnlyList<string>> GetPlaylistTrackIdsAsync(string playlistId, CancellationToken token);
    Task<PlaylistCreationResult> CreatePlaylistAsync(PlaylistCreationRequest request, IProgress<int>? progress, CancellationToken token);
}

public enum PlaylistVisibility { Unlisted, Public, Private }
public enum PlaylistCreationStatus { Completed, Failed, Partial, Cancelled, Uncertain }

public sealed record OwnedPlaylist(string Id, string Name, Uri Url);

public sealed record PlaylistCreationRequest(string Name, string Description, PlaylistVisibility Visibility, IReadOnlyList<string> TrackIds, OwnedPlaylist? Target = null);

/// <summary>Added counts only acknowledged items. An uncertain request may have applied more than this count.</summary>
public sealed record PlaylistCreationResult(Uri? Url, int Added, int Total, PlaylistCreationStatus Status, string? Message = null, int Skipped = 0);
