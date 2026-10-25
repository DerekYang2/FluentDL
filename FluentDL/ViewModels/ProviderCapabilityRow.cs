namespace FluentDL.ViewModels;

public enum CapabilityState { Ready, Limited, Unavailable, NotApplicable, Unknown }

public sealed record ProviderCapability(CapabilityState State, string Details)
{
    public string Label => State switch
    {
        CapabilityState.Ready => "Ready",
        CapabilityState.Limited => "Limited",
        CapabilityState.Unavailable => "Unavailable",
        CapabilityState.Unknown => "Not checked",
        _ => "\u2014",
    };
    public bool IsReady => State == CapabilityState.Ready;
    public bool IsWarning => State is CapabilityState.Limited or CapabilityState.Unknown;
    public bool IsUnavailable => State == CapabilityState.Unavailable;
    public string Description => $"{Label}. {Details}";
}

public sealed record ProviderAccessSnapshot(
    bool DeezerSignedIn = false, bool DeezerLossless = false,
    bool QobuzCatalog = false, bool QobuzSignedIn = false, bool QobuzFullTracks = false, bool QobuzLossless = false,
    bool SpotifyCatalog = false, bool SpotifySignedIn = false,
    bool TidalCatalog = false, bool TidalSignedIn = false, bool? TidalSubscription = null,
    bool? DeezerPlaylistReadAccess = null);

public sealed record ProviderCapabilityRow(string Provider, ProviderCapability Search, ProviderCapability Downloads,
    ProviderCapability PlaylistUrls, ProviderCapability PlaylistUpload)
{
    public string SourceId => Provider.ToLowerInvariant();
    public string Description => $"{Provider}. Search: {Search.Label}. Playlist URLs: {PlaylistUrls.Label}. Downloads: {Downloads.Label}. Playlist upload: {PlaylistUpload.Label}.";

    private static ProviderCapability Account(bool signedIn) => signedIn
        ? new(CapabilityState.Ready, "The user account is signed in.")
        : new(CapabilityState.Unavailable, "Sign in through this provider's Settings section.");

    private static ProviderCapability Catalog(bool connected) => connected
        ? new(CapabilityState.Ready, "Catalog access is configured.")
        : new(CapabilityState.Unavailable, "Catalog access is not configured or authentication failed.");

    private static ProviderCapability Import(bool catalog, bool user) => user ? Account(true)
        : catalog ? new(CapabilityState.Limited, "Public playlists only. Sign in for private playlists.") : Catalog(false);

    public static IReadOnlyList<ProviderCapabilityRow> Create(ProviderAccessSnapshot access) =>
    [
        new("Deezer", new(CapabilityState.NotApplicable, "Public search needs no account sign-in."),
            !access.DeezerSignedIn ? Account(false) : access.DeezerLossless ? Account(true)
                : new(CapabilityState.Limited, "This account is limited to 128 kbps MP3."),
            DeezerImport(access), Account(access.DeezerSignedIn)),
        new("Qobuz", Catalog(access.QobuzCatalog),
            !access.QobuzSignedIn ? Account(false) : !access.QobuzFullTracks
                ? new(CapabilityState.Unavailable, "This account has no full-track streaming entitlement. Samples do not count as full downloads.")
                : access.QobuzLossless ? Account(true) : new(CapabilityState.Limited, "Full tracks are available, but lossless streaming is not enabled for this account."),
            Import(access.QobuzCatalog, access.QobuzSignedIn), Account(access.QobuzSignedIn)),
        new("Spotify", Catalog(access.SpotifyCatalog), new(CapabilityState.NotApplicable, "Direct Spotify downloads do not apply. Equivalent-track downloads use other providers."),
            Import(access.SpotifyCatalog, access.SpotifySignedIn), Account(access.SpotifySignedIn)),
        new("TIDAL", Catalog(access.TidalCatalog), !access.TidalSignedIn ? Account(false)
            : access.TidalSubscription == true ? Account(true)
            : access.TidalSubscription == false ? new(CapabilityState.Unavailable, "This account has no active full-track subscription or trial.")
            : new(CapabilityState.Unknown, "The account is saved, but its full-track subscription could not be checked."),
            Import(access.TidalCatalog, access.TidalSignedIn), Account(access.TidalSignedIn)),
    ];

    private static ProviderCapability DeezerImport(ProviderAccessSnapshot access) => !access.DeezerSignedIn
        ? new(CapabilityState.Limited, "Public playlist links only. Sign in with your Deezer ARL to load private playlists your account can access.")
        : access.DeezerPlaylistReadAccess switch
        {
            true => new(CapabilityState.Ready, "Signed-in playlist access was checked. Public playlists and private playlists your account can access are supported. Individual playlist permissions still apply."),
            false => new(CapabilityState.Limited, "The signed-in playlist access check failed. Public playlist links remain supported. Try signing in to Deezer again."),
            _ => new(CapabilityState.Unknown, "Signed in, but playlist access has not been checked. Private playlist access is not confirmed."),
        };
}
