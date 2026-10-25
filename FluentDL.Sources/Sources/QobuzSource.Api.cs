using FluentDL.Core.Helpers;
using FluentDL.Core.Matching;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using QobuzApiSharp.Models.Content;
using QobuzApiSharp.Models.User;
using QobuzApiSharp.Service;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;


namespace FluentDL.Services.Sources;

internal sealed partial class QobuzSource
{
    private QobuzApiService apiService = null!;
    private HttpClient QobuzHttpClient = null!;

    private string GetFullTitle(Track? track)
    {
        if (track == null) 
            return "";

        if (string.IsNullOrWhiteSpace(track.Version))
            return track.Title;

        return $"{track.Title} ({track.Version})";
    }

    public string oldI = "VuCHDsuyiFjcl994xa1eyg==";
    public string oldS = "5mLYFjeXUrtSoZvPIYn7ymMz6QQY65+XBg2OBH9cxLJlT9hMiDIrRB8Yj4OfOikn";
    public bool IsInitialized = false;
    private Login? loginResult = null;

    public void Initialize(string? userId, string? AuthToken, string? appId, string? appSecret, AuthenticationCallback? authCallback = null)
    {
        IsInitialized = false;
        string? failMessage = null;
        loginResult = null;

        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(AuthToken))  // Token route
        {
            if (!string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(appSecret)) // Custom app credentials
            {
                try
                {
                    apiService = new QobuzApiService(appId, appSecret);
                    loginResult = apiService.LoginWithToken(userId, AuthToken);

                    if (!apiService.IsAppSecretValid())
                        throw new Exception("Invalid app secret");

                    IsInitialized = true;
                    Debug.WriteLine("Qobuz initialized custom");
                    authCallback?.Invoke(StatusLevel.Success, "Logged in using custom app ID/secret");
                }
                catch
                {
                    failMessage = "Failed to log in using custom app ID/secret";
                    loginResult = null;
                }
            }
            else
            {
                try
                {
                    apiService = new QobuzApiService();
                    loginResult = apiService.LoginWithToken(userId, AuthToken);
                    IsInitialized = true;
                    authCallback?.Invoke(StatusLevel.Success, "Logged in using latest app ID/secret");
                }
                catch
                {
                    // Try intialize using old app credentials
                    try
                    {
                        apiService = new QobuzApiService(AesHelper.Decrypt(oldI), AesHelper.Decrypt(oldS));
                        loginResult = apiService.LoginWithToken(userId, AuthToken);

                        if (!apiService.IsAppSecretValid())
                            throw new Exception("Invalid app secret");

                        IsInitialized = true;
                        Debug.WriteLine("Qobuz initialized (old)");
                        authCallback?.Invoke(StatusLevel.Success, "Logged in using old app ID/secret");
                    }
                    catch
                    {
                        failMessage = "Failed to log in using ID/token";
                        loginResult = null;
                    }
                }
            }
        }

        try
        {
            apiService ??= new QobuzApiService();
            InitializeQobuzHttpClient(apiService.AppId, apiService.UserAuthToken);
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed to initialize Qobuz HTTP client: " + e.Message);
            IsInitialized = false;
            failMessage = "Failed to initialize HTTP client: " + e.Message;
            loginResult = null;
        }

        if (!IsInitialized)
        {
            authCallback?.Invoke(StatusLevel.Error, failMessage ?? "No login methods attempted");
        }
    }

    public string? LoginString()
    {
        if (loginResult == null)
        {
            return null;
        }

        string str = string.Empty;
        str += "Account ID: " + loginResult.User.Id + "\n";
        str += "App ID: " + apiService.AppId + "\n";
        str += "User Zone: " + loginResult.User.Zone + "\n";
        str += "Qobuz Credential Description: " + loginResult.User.Credential.Description + "\n";

        if (loginResult.User.Subscription != null)
        {
            str += "Offer Type: " + loginResult.User.Subscription.Offer + "\n";
            str += "Start Date: ";
            str += loginResult.User.Subscription.StartDate != null ? ((DateTimeOffset)loginResult.User.Subscription.StartDate).ToString("yyyy-MM-dd") : "?";
            str += "\n";
            str += "End Date: ";
            str += loginResult.User.Subscription.EndDate != null ? ((DateTimeOffset)loginResult.User.Subscription.EndDate).ToString("yyyy-MM-dd") : "?";
            str += "\n";
            str += "Periodicity: " + loginResult.User.Subscription.Periodicity;
        }
        else if (loginResult.User?.Credential?.Parameters?.Source == "household" && loginResult.User.Credential.Parameters?.HiresStreaming == true)
        {
            str += "Active Family sub-account, unknown End Date \n";
            str += "Credential Label: " + loginResult.User.Credential.Label;
        }
        else
        {
            str += "Subscription: none";
        }
        var access = StreamingAccess;
        str += "\nStreaming entitlement: " + (access.Lossless ? "lossless" : access.FullTracks ? "lossy only" : "not enabled");
        return str;
    }

    public bool IsPremium()
    {
        if (!IsInitialized || loginResult == null)
        {
            return false;
        }
        if (loginResult?.User?.Subscription != null)
        {
            return true;
        }
        else if (loginResult?.User?.Credential?.Parameters?.Source == "household" && loginResult.User.Credential.Parameters?.HiresStreaming == true)
        {
            return true;
        }
        return false;
    }

    public async Task GeneralSearch(ICollection<SongSearchObject> itemSource, string query, CancellationToken token, int offset = 0, int limit = 25, bool albumMode = false)
    {
        query = query.Trim(); // Trim the query
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        try
        {
            if (albumMode)
            {   
                await foreach (var album in ApiSearch<Album>(query, offset, limit).WithCancellation(token))
                {
                    if (token.IsCancellationRequested) return;
                    var albumObj = ConvertAlbumSearchObject(album);
                    if (albumObj != null)
                    {
                        itemSource.Add(albumObj);
                    }
                }
            }
            else
            {
                await foreach (var track in ApiSearch<Track>(query, offset, limit).WithCancellation(token))
                {
                    if (token.IsCancellationRequested) return;
                    var song = ConvertSongSearchObject(track);
                    if (song != null)
                    {
                        itemSource.Add(song);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine("General search failed: " + e.Message);
        }
    }

    private bool CloseMatch(string str1, string str2)
    {
        return FuzzyMatch.IsSubstring(str1.ToLower(), str2.ToLower());
    }

    public async Task AdvancedSearch(ICollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, CancellationToken token, int offset = 0, int limit = 25, bool clearResults = true)
    {
        // Qobuz doesn't have an advanced search, must be done manually
        artistName = artistName.Trim();
        trackName = trackName.Trim();
        albumName = albumName.Trim();

        if (string.IsNullOrWhiteSpace(artistName) && string.IsNullOrWhiteSpace(trackName) && string.IsNullOrWhiteSpace(albumName))
        {
            return;
        }

        if (clearResults)
        {
            itemSource.Clear(); // Clear the item source
        }

        bool isArtistSpecified = !string.IsNullOrWhiteSpace(artistName);
        bool isTrackSpecified = !string.IsNullOrWhiteSpace(trackName);

        var trackIdList = new HashSet<int>();

        if (!string.IsNullOrWhiteSpace(albumName))
        {
            var albumResults = new List<Album>();
            await foreach (var album in ApiSearch<Album>(albumName, offset, limit).WithCancellation(token))
            {
                albumResults.Add(album);
            }

            if (token.IsCancellationRequested) return; // Check if task is cancelled

            // Add if album name match
            foreach (var album in albumResults)
            {
                if (token.IsCancellationRequested) return; // Check if task is cancelled

                // Ensure artist matches to quickly filter out irrelevant albums
                var oneArtistMatch = false;
                if (isArtistSpecified)
                {
                    foreach (var artist in album.Artists) // For every artist in album
                    {
                        if (CloseMatch(artistName, artist.Name))
                        {
                            oneArtistMatch = true;
                            break;
                        }
                    }
                }
                else
                {
                    oneArtistMatch = true; // Assume true if artist not specified
                }

                if (oneArtistMatch && CloseMatch(albumName, album.Title))
                {
                    var fullAlbumObj = await Task.Run(() => apiService.GetAlbum(album.Id, withAuth: true), token);

                    if (fullAlbumObj == null || fullAlbumObj.Tracks == null || fullAlbumObj.Tracks.Items.Count == 0) continue;

                    foreach (var track in fullAlbumObj.Tracks.Items)
                    {
                        if (track.Id == null) continue;

                        bool valid = true;
                        if (isArtistSpecified) // Album and artist specified
                        {
                            var trackArtists = GetAllContributorsList(track.Performers);
                            oneArtistMatch = trackArtists.Any(x => CloseMatch(artistName, x)); // AT least one artist match

                            if (isTrackSpecified) // Album, artist, and track specified
                            {
                                valid = oneArtistMatch && CloseMatch(trackName, GetFullTitle(track));
                            }
                            else // Album and artist specified
                            {
                                valid = oneArtistMatch; // Different case for validity
                            }
                        }
                        else if (isTrackSpecified) // Track name and artist specified
                        {
                            valid = CloseMatch(trackName, GetFullTitle(track));
                        }

                        var id = track.Id.GetValueOrDefault();

                        if (valid && !trackIdList.Contains(id)) // Check if track is already added
                        {
                            var trackObj = await GetTrackAsync(id);
                            if (trackObj == null) 
                                continue; 
                            itemSource.Add(trackObj);
                            trackIdList.Add(id);
                            if (trackIdList.Count >= limit) 
                                break;
                        }
                    }
                }

                if (trackIdList.Count >= limit) break;
            }
        }
        else
        {
            if (isTrackSpecified && isArtistSpecified) // If artist and track are specified
            {
                await foreach (var track in ApiSearch<Track>(artistName + " " + trackName, offset, limit).WithCancellation(token))
                {
                    if (token.IsCancellationRequested) return; // Check if task is cancelled
                    if (track.Id == null) continue;

                    var trackArtists = GetAllContributorsList(track.Performers);

                    // At least one artist close match
                    if (trackArtists.Any(x => CloseMatch(artistName, x)))
                    {
                        var id = track.Id.GetValueOrDefault();
                        if (!trackIdList.Contains(id)) // Add this track to the item source
                        {
                            itemSource.Add(ConvertSongSearchObject(track));
                            trackIdList.Add(id);
                        }
                    }
                }
            }
            else
            {
                var query = isTrackSpecified ? trackName : artistName; 
                var searchType = isTrackSpecified ? EnumQobuzSearchType.ByReleaseName : EnumQobuzSearchType.ByMainArtist;
                await foreach (var track in ApiSearch<Track>(query, offset, limit, searchType).WithCancellation(token))
                {
                    if (token.IsCancellationRequested) return; // Check if task is cancelled
                    if (track.Id == null) continue;

                    var id = track.Id.GetValueOrDefault();
                    if (!trackIdList.Contains(id)) // Add this track to the item source
                    {
                        itemSource.Add(ConvertSongSearchObject(track));
                        trackIdList.Add(id);
                    }
                }
            }
        }
    }

    public async Task AddTracksFromLink(ICollection<SongSearchObject> itemSource, string url, CancellationToken token, UrlStatusUpdateCallback? statusUpdate, bool albumMode = false)
    {
        var isTrack = url.StartsWith("https://play.qobuz.com/track/") || url.StartsWith("https://open.qobuz.com/track/") || Regex.IsMatch(url, @"https://www\.qobuz\.com(/[^/]+)?/track/.*");
        var isAlbum = url.StartsWith("https://play.qobuz.com/album/") || url.StartsWith("https://open.qobuz.com/album/") || Regex.IsMatch(url, @"https://www\.qobuz\.com(/[^/]+)?/album/.*");
        var isPlaylist = url.StartsWith("https://play.qobuz.com/playlist/") || url.StartsWith("https://open.qobuz.com/playlist/") || Regex.IsMatch(url, @"https://www\.qobuz\.com(/[^/]+)?/playlist/.*"); // Remove any query parameters

        url = url.Split('?')[0]; // Remove any query parameters
        var id = url.Split('/').Last(); // Get string after the last slash

        if (isTrack)
        {
            var track = await Task.Run(() =>
            {
                try
                {
                    return apiService.GetTrack(id, withAuth: true);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to get qobuz track: " + e.Message);
                    return null;
                }
            }, token);

            if (track != null) {
                itemSource.Add(await Task.Run(() => ConvertSongSearchObject(track)));
                statusUpdate?.Invoke(StatusLevel.Success, $"<b>Qobuz</b>   Loaded track <a href='{url}'>{GetFullTitle(track)}</a>"); 
            }
            else
            {
                statusUpdate?.Invoke(StatusLevel.Error, $"<b>Qobuz</b>   Track may not exist or is private.");
            }
        }
        else if (isAlbum)
        {
            var album = await Task.Run(() =>
            {
                try
                {
                    return apiService.GetAlbum(id, withAuth: true);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to get qobuz album: " + e.Message);
                    return null;
                }
            }, token);

            if (album != null && album.Tracks != null && album.Tracks.Items.Count > 0)
            {
                statusUpdate?.Invoke(StatusLevel.Informational, $"<b>Qobuz</b>   Loading album <a href='{url}'>{album.Title}</a>", -1); // Show an informational message

                if (albumMode)
                {
                    var albumObj = ConvertAlbumSearchObject(album);
                    if (albumObj != null)
                    {
                        itemSource.Add(albumObj);
                    }
                }
                else
                {
                    foreach (var track in album.Tracks.Items)
                    {
                        if (token.IsCancellationRequested)
                        {
                            statusUpdate?.Invoke(StatusLevel.Warning, $"<b>Qobuz</b>   Cancelled loading album <a href='{url}'>{album.Title}</a>"); // Show a warning message
                            return;
                        }

                        itemSource.Add(CreateSongSearchObject(track, album));
                    }
                }

                statusUpdate?.Invoke(StatusLevel.Success, $"<b>Qobuz</b>   Loaded album <a href='{url}'>{album.Title}</a>"); // Show a success message
            }
            else
            {
                statusUpdate?.Invoke(StatusLevel.Error, $"<b>Qobuz</b>   Album may not exist or is private.");
            }
        }
        else if (isPlaylist)
        {
            await ImportPlaylistAsync(itemSource, url, id, statusUpdate, token);
        }
    }

    public enum QobuzLinkType { None, Label, Artist }

    public bool IsLinkLabelOrArtist(string url, out QobuzLinkType linkType, out string? id)
    {
        linkType = QobuzLinkType.None;
        id = null;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Accept only qobuz.com and its subdomains
        var host = uri.Host;
        if (!host.Equals("qobuz.com", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".qobuz.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = uri.AbsolutePath;

        // Matches /label/[slugs/](\d+)[/extra/paths]
        var labelPattern = new Regex(
            @"^/(?:[a-z]{2}(?:-[a-z]{2})?/)?label/(?:.*?/)?(\d+)(?:/.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Matches /(artist|interpreter)/[slugs/](\d+)[/extra/paths]
        var artistPattern = new Regex(
            @"^/(?:[a-z]{2}(?:-[a-z]{2})?/)?(?:artist|interpreter)/(?:.*?/)?(\d+)(?:/.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var labelMatch = labelPattern.Match(path);
        if (labelMatch.Success)
        {
            linkType = QobuzLinkType.Label;
            id = labelMatch.Groups[1].Value;
            return true;
        }

        var artistMatch = artistPattern.Match(path);
        if (artistMatch.Success)
        {
            linkType = QobuzLinkType.Artist;
            id = artistMatch.Groups[1].Value;
            return true;
        }

        return false;
    }

    public async Task AddTracksFromLabelOrArtist(ICollection<SongSearchObject> itemSource, string url, CancellationToken token, UrlStatusUpdateCallback? statusUpdate, int offset, int limit, bool albumMode = false)
    {
        if (token.IsCancellationRequested)
        {
            statusUpdate?.Invoke(StatusLevel.Warning, $"<b>Qobuz</b>   Cancelled loading albums from <a href='{url}'>{url}</a>"); // Show a warning message
            return;
        }

        QobuzLinkType linkType;
        string? id;
        if (!IsLinkLabelOrArtist(url, out linkType, out id) || string.IsNullOrEmpty(id))
        {
            return;
        }

        if (linkType == QobuzLinkType.Label)
        {
            // Can only be album
            var labelResults = await Task.Run(() =>
            {
                try
                {
                    return apiService.GetLabel(id, withAuth: true, "albums", limit, offset);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to get qobuz label: " + e.Message);
                    return null;
                }
            });

            if (labelResults?.Albums?.Items != null)
            {

                foreach (var album in labelResults.Albums.Items)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    var albumObj = ConvertAlbumSearchObject(album);
                    if (albumObj != null)
                    {
                        itemSource.Add(albumObj);
                    }
                    //var fullAlbumObj = await Task.Run(() => apiService.GetAlbum(album.Id, withAuth: true), token);
                    //if (fullAlbumObj == null || fullAlbumObj.Tracks == null || fullAlbumObj.Tracks.Items.Count == 0) continue;
                    //foreach (var track in fullAlbumObj.Tracks.Items)
                    //{
                    //    if (track.Id == null) continue;
                    //    itemSource.Add(CreateSongSearchObject(track, fullAlbumObj));
                    //}
                }
            } else
            {
                statusUpdate?.Invoke(StatusLevel.Error, $"<b>Qobuz</b>   Label may not exist or is private.");
            }

        }
        else if (linkType == QobuzLinkType.Artist)
        {
            // Do only albums for now
            // TODO: look into apiService.GetReleaseList("artist_id", true, "album, epSingle", "relevant", null, 30, limit, offset);

            var artistResults = await Task.Run(() =>
            {
                try
                {
                    return apiService.GetArtist(id, withAuth: true, "albums", "official", limit, offset);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to get qobuz artist: " + e.Message);
                    return null;
                }
            });

            if (artistResults?.Albums?.Items != null)
            {
                foreach (var album in artistResults.Albums.Items)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    var albumObj = ConvertAlbumSearchObject(album);
                    if (albumObj != null)
                    {
                        itemSource.Add(albumObj);
                    }
                }
            }
            else
            {
                statusUpdate?.Invoke(StatusLevel.Error, $"<b>Qobuz</b>   Artist may not exist or is private.");
            }
        }
    }

    public SongSearchObject ConvertSongSearchObject(Track track, string? mainArtist = null)
    {
        var listedArtist = track.Performer?.Name ?? "unlisted";
        var contribList = GetAllContributorsList(track.Performers);

        if ((contribList == null || contribList.Count == 0) && !string.IsNullOrWhiteSpace(mainArtist)) // If no contributors, fallback
        {
            contribList = [mainArtist];
        }   
        contribList ??= [];

        if (contribList.Contains(listedArtist)) // Move listed artist to the front
        {
            contribList.Remove(listedArtist);
            contribList.Insert(0, listedArtist);
        }

        return new SongSearchObject()
        {
            AlbumName = track.Album?.Title ?? "",
            Artists = string.Join(", ", contribList),
            Duration = track.Duration?.ToString() ?? "0",
            Explicit = track.ParentalWarning ?? false,
            Source = SourceIds.Qobuz,
            Id = track.Id?.ToString() ?? "",
            TrackPosition = (track.TrackNumber ?? 1).ToString(),
            ImageLocation = track.Album?.Image?.Small ?? "",
            Rank = (track.Album?.Popularity ?? 0).ToString(),
            ReleaseDate = track.ReleaseDateOriginal.GetValueOrDefault().ToString("yyyy-MM-dd"),
            Title = GetFullTitle(track),
            Isrc = track.Isrc
        };
    }

    public SongSearchObject CreateSongSearchObject(Track track, Album album)
    {
        var listedArtist = track.Performer?.Name ?? "unlisted";
        var contribList = GetAllContributorsList(track.Performers);

        if ((contribList == null || contribList.Count == 0) && !string.IsNullOrEmpty(album.Artist?.Name))
        {
            contribList = [album.Artist.Name];
        }
        contribList ??= [];
        if (contribList.Contains(listedArtist)) // Move listed artist to the front
        {
            contribList.Remove(listedArtist);
            contribList.Insert(0, listedArtist);
        }

        return new SongSearchObject()
        {
            AlbumName = album.Title,
            Artists = string.Join(", ", contribList),
            Duration = track.Duration?.ToString() ?? "0",
            Explicit = track.ParentalWarning ?? false,
            Source = SourceIds.Qobuz,
            Id = track.Id.GetValueOrDefault().ToString(),
            TrackPosition = track.TrackNumber.GetValueOrDefault().ToString(),
            ImageLocation = album.Image.Small,
            Rank = (album.Popularity ?? 0).ToString(),
            ReleaseDate = track.ReleaseDateOriginal.GetValueOrDefault().ToString("yyyy-MM-dd"),
            Title = GetFullTitle(track),
            Isrc = track.Isrc
        };
    }

    public AlbumSearchObject ConvertAlbumSearchObject(Album album)
    {
        var mainArtist = album.Artist.Name;
        var artists = album.Artists.Select(a => a.Name).ToList();

        if (artists.Count == 0)
        {
            artists.Add("unlisted");
        }

        // Ensure main artist appears first
        artists.Remove(mainArtist);
        artists.Insert(0, mainArtist);

        return new AlbumSearchObject()
        {
            AlbumName = album.Title,
            Artists = string.Join(", ", artists),
            Duration = album.Duration?.ToString() ?? "0",
            Explicit = album.ParentalWarning ?? false,
            Source = SourceIds.Qobuz,
            Id = album.Id.ToString(),
            TrackPosition = "1", // Not applicable for albums
            ImageLocation = album.Image?.Small ?? "",
            Rank = (album.Popularity ?? 0).ToString(),
            ReleaseDate = album.ReleaseDateOriginal.GetValueOrDefault().ToString("yyyy-MM-dd"),
            Title = album.Title,
            Isrc = album.Upc,
            TracksCount = album.TracksCount ?? 0,
        };
    }

    public async Task<Album?> GetInternalAlbum(string id)
    {
        return await Task.Run(() =>
        {
            try
            {
                return apiService.GetAlbum(id, withAuth: true);
            }
            catch
            {
                return null;
            }
        });
    }

    public async Task<Track?> GetInternalTrack(string id)
    {
        return await Task.Run(() =>
        {
            try
            {
                return apiService.GetTrack(id, withAuth: true);
            }
            catch
            {
                return null;
            }
        });
    }

    public async Task<SongSearchObject?> GetTrackAsync(int? id, CancellationToken token = default)
    {
        if (id == null) return await Task.FromResult<SongSearchObject?>(null);
        return await GetTrackAsync(id.ToString(), token);
    }

    public async Task<SongSearchObject?> GetTrackAsync(string? id, CancellationToken token = default)
    {
        if (id == null) return null;
        var track = await GetCatalogAsync<Track>("track/get?track_id=" + Uri.EscapeDataString(id), token);
        if (track == null)
        {
            return null;
        }

        return ConvertSongSearchObject(track);
    }

    /// <param name="isrc">The ISRC to match by first. ConversionService looks it up for songs that don't carry one.</param>
    public async Task<SongSearchObject?> GetQobuzTrack(SongSearchObject songObj, string? isrc, CancellationToken token = default, ConversionUpdateCallback? callback = null, bool onlyISRC = false)
    {
        if (!IsInitialized) // Report it, so the queue marks the row (bug 7)
        {
            callback?.Invoke(StatusLevel.Error, songObj);
            return null;
        }

        string query = songObj.Artists.Split(", ")[0] + " " + songObj.Title;

        // First attempt exact isrc search
        if (isrc != null)
        {
            var foundTrack = await SearchISRC(isrc, token);
            if (foundTrack != null)
            {
                var retObj = ConvertSongSearchObject(foundTrack);
                callback?.Invoke(StatusLevel.Success, retObj); // Show a success message
                return retObj;
            }
        }

        if (onlyISRC) // If only ISRC is allowed, return null
        {
            callback?.Invoke(StatusLevel.Error, songObj); // Show an error message with original object
            return null;
        }

        // BELOW: try matching by metadata
        // Convert artists csv to array
        var artists = songObj.Artists.Split(", ");
        for (int i = 0; i < artists.Length; i++)
        {
            artists[i] = artists[i].ToLower();
        }


        var albumName = songObj.AlbumName;
        var trackName = songObj.Title;

        // Search by album
        var albumResults = await GetCatalogAsync<SearchResult>("album/search?query=" + Uri.EscapeDataString(albumName) + "&limit=5&offset=0", token);

        if (token.IsCancellationRequested) return null; // Check if task is cancelled

        foreach (var album in albumResults?.Albums.Items ?? [])
        {
            if (token.IsCancellationRequested) return null; // Check if task is cancelled

            // Ensure artist matches before checking tracks
            var oneArtistMatch = false;
            foreach (var artist in artists) // For every artist in SongObject
            {
                foreach (var albumArtist in album.Artists) // For every artist in album
                {
                    if (CloseMatch(artist, albumArtist.Name))
                    {
                        oneArtistMatch = true;
                        break;
                    }
                }
            }

            if (oneArtistMatch && CloseMatch(albumName, album.Title)) // Album close match
            {
                var fullAlbumObj = await GetCatalogAsync<Album>("album/get?album_id=" + Uri.EscapeDataString(album.Id) + "&limit=1200&offset=0", token);

                if (fullAlbumObj == null || fullAlbumObj.Tracks == null || fullAlbumObj.Tracks.Items.Count == 0) continue;

                foreach (var track in fullAlbumObj.Tracks.Items)
                {
                    if (track.Id == null) continue;

                    oneArtistMatch = false;
                    foreach (var artist in artists) // For every artist in SongObject
                    {
                        var a1 = TextNormalization.PrunePunctuation(artist.ToLower());
                        var performerPrune = TextNormalization.PrunePunctuation(track.Performers?.ToLower() ?? "");
                        if (performerPrune.Contains(a1)) // Check if artist is in performers
                        {
                            oneArtistMatch = true;
                            break;
                        }
                    }

                    if (oneArtistMatch && CloseMatch(trackName, GetFullTitle(track))) // Album close match, artist match, title close match
                    {
                        var candidate = await GetCatalogAsync<Track>("track/get?track_id=" + track.Id.Value, token);
                        if (IsAvailableConversionTrack(candidate))
                        {
                            var retObj = ConvertSongSearchObject(candidate!);
                            callback?.Invoke(StatusLevel.Warning, retObj); // Not found by ISRC
                            return retObj;
                        }
                    }
                }
            }
        }

        // Try searching without album, same as above
        if (token.IsCancellationRequested) return null; // Check if task is cancelled

        await foreach (var result in ApiSearch<Track>(query, limit: 28, cancellationToken: token))
        {
            if (token.IsCancellationRequested) return null;
            if (!IsAvailableConversionTrack(result)) continue;

            // Check if at least one pair of artists match
            foreach (var artist in artists)
            {
                var a1 = TextNormalization.PrunePunctuation(artist.ToLower());
                var performerPrune = result.Performers == null ? "" : TextNormalization.PrunePunctuation(result.Performers.ToLower());
                if (performerPrune.Contains(a1) && TextNormalization.PrunePunctuation(trackName.ToLower()).Equals(TextNormalization.PrunePunctuation(GetFullTitle(result).ToLower())))
                {
                    var retObj = ConvertSongSearchObject(result);
                    callback?.Invoke(StatusLevel.Warning, retObj); // Not found by ISRC
                    return retObj;
                }
            }
        }

        callback?.Invoke(StatusLevel.Error, songObj); // Show an error message with original object
        return null;
    }

    // Search results can retain withdrawn IDs that match the ISRC but return 404 when fetched or added to a playlist.
    private static bool IsAvailableConversionTrack(Track? track) =>
        track is { Id: > 0, Streamable: true, Displayable: true };

    public string? GetPreviewUri(string trackId)
    {
        try
        {
            return apiService.GetTrackFileUrl(trackId, "5")?.Url;
        }
        catch
        {
            return null;
        }
    }

    public List<string> GetAllContributorsList(string performerStr)
    {
        if (string.IsNullOrWhiteSpace(performerStr))  // Issue with QobuzAPI, should be nullable but is not
        {
            return new List<string>();
        }

        var performers = performerStr
            .Split(new string[] { " - " }, StringSplitOptions.None)
            .Select(performer => performer.Split(',')) // Split name & roles in best effort by ',', first part is name, next parts roles
            .GroupBy(parts => parts[0].Trim()) // Group performers by name since they can occur multiple times
            .ToDictionary(group => group.Key, group => group.SelectMany(parts => parts.Skip(1).Select(role => role.Trim())).Distinct().ToList()); // Flatten roles by performer and remove duplicates
        var artistRoles = new HashSet<string>
        {
            "MainArtist",
            "main-artist",
            "Performer",
            "FeaturedArtist",
            "Featuring",
            "featured-artist"
        };

        var artistList = new SortedSet<string>();

        foreach (var performer in performers) // For every performer
        {
            var name = performer.Key;
            var roles = performer.Value; // List of the roles for this person

            if (roles.Any(role => artistRoles.Contains(role))) // If at least one role is an artist role
            {
                artistList.Add(name);
            }
        }

        return artistList.ToList();
    }

    public async Task<string> DownloadTrack(string filePath, SongSearchObject song, IProgress<ProgressData> progress, string? format = null)
    {
        // Remove extension if it exists
        filePath = AudioFiles.RemoveExtension(filePath);

        if (format == null) // If format is not specified, get it from the settings
        {
            // Get the format from settings
            var selectedIndex = await settings.ReadSettingAsync<int?>(SettingKeys.QobuzQuality) ?? 0;

            format = selectedIndex switch
            {
                0 => "5",
                1 => "6",
                2 => "7",
                3 => "27",
                _ => "6" // Flac, 16/44.1 as default
            };
        }
        // Force mp3 if no premium
        if (!IsPremium())
        {
            format = "5"; // mp3
        }

        // Add the extension
        filePath += "." + (format == "5" ? "mp3" : "flac"); // Only one format is mp3

        if (System.IO.File.Exists(filePath) && await settings.ReadSettingAsync<bool>(SettingKeys.Overwrite) == false)
        {
            throw new Exception("File already exists");
        }

        var fileUrl = apiService.GetTrackFileUrl(song.Id, format);
        await WebDownloads.DownloadFileAsync(filePath, fileUrl.Url, progress);
        return filePath;
        //var trackBytes = await new HttpClient().GetByteArrayAsync(fileUrl.Url);
        //await File.WriteAllBytesAsync(filePath, trackBytes);
    }

    /**
     * Qobuz genre list sometimes includes sub-genres separately, such as
     * Musiques du monde | Musiques du monde→Europe | Musiques du monde→Europe→Chanson française
     */
    public List<string> PruneGenreList(List<string> rawGenreList)
    {
        var genreList = new List<string>();
        foreach (var genre in rawGenreList)
        {
            var genreArray = genre.Split("\u2192");
            genreList.Add(genreArray.Last().Trim());
        }

        return genreList.ToList();
    }

    public async Task UpdateMetadata(string filePath, string trackId)
    {
        var track = await GetInternalTrack(trackId);

        var listedArtist = track?.Performer?.Name ?? "unlisted";
        var contribList = GetAllContributorsList(track?.Performers ?? "");
        if ((contribList == null || contribList.Count == 0) && !string.IsNullOrWhiteSpace(track?.Album?.Artist?.Name))
        {
            contribList = [track.Album.Artist.Name];
        }

        contribList ??= [];

        if (contribList.Contains(listedArtist)) // Move listed artist to the front
        {
            contribList.Remove(listedArtist);
            contribList.Insert(0, listedArtist);
        }

        var metadata = new MetadataObject(filePath)
        {
            Title = GetFullTitle(track),
            Artists = contribList.ToArray(),
            Genres = PruneGenreList(track?.Album?.GenresList ?? []).ToArray(),
            AlbumName = track?.Album?.Title ?? "",
            AlbumArtists = track?.Album?.Artists?.Select(x => x.Name)?.ToArray() ?? [],
            Isrc = track?.Isrc ?? "",
            ReleaseDate = track?.ReleaseDateOriginal.GetValueOrDefault().Date ?? DateTime.MinValue,
            TrackNumber = track?.TrackNumber.GetValueOrDefault() ?? 1,
            TrackTotal = track?.Album?.TracksCount ?? 0,
            Upc = track?.Album?.Upc ?? "",
            AlbumArtPath = track?.Album?.Image.Large ?? "",
        };
        await metadata.SaveAsync();

        /*
        var tfile = TagLib.File.Create(filePath);
        tfile.Mode = File.AccessMode.Write;

        TagLib.Ogg.XiphComment custom = (TagLib.Ogg.XiphComment)tfile.GetTag(TagLib.TagTypes.Xiph);

        tfile.Tag.Title = track.Title;
        tfile.Tag.Album = track.Album.Title;
        //atlTrack.AlbumArtist = albumArtistStr;
        tfile.Tag.Performers = contribList.ToArray();

        // Release Year tag (The "tfile.Tag.Year" field actually writes to the DATE tag, so use custom tag)
        custom.SetField("YEAR", releaseDateString.Substring(0, 4));

        // Release Date tag
        custom.SetField("DATE", releaseDateString);


        tfile.Tag.Track = Convert.ToUInt32(track.TrackNumber);
        // Override TRACKNUMBER tag again to prevent using "two-digit zero-filled value"
        // See https://github.com/mono/taglib-sharp/pull/240 where this change was introduced in taglib-sharp v2.3
        custom.SetField("TRACKNUMBER", Convert.ToUInt32(track.TrackNumber));

        tfile.Tag.TrackCount = Convert.ToUInt32(track.Album.TracksCount);
        //atlTrack.Genre = genreStr;
        tfile.Tag.ISRC = track.Isrc;

        custom.SetField("UPC", track.Album.Upc);

        // Get image bytes for cover art
        var imageBytes = await WebDownloads.GetBytesAsync(track.Album.Image.Large);

        // Define cover art to use for FLAC file(s)
        TagLib.Id3v2.AttachmentFrame pic = new TagLib.Id3v2.AttachmentFrame { TextEncoding = TagLib.StringType.Latin1, MimeType = System.Net.Mime.MediaTypeNames.Image.Jpeg, Type = TagLib.PictureType.FrontCover, Data = new ByteVector(imageBytes) };

        // Save cover art to FLAC file.
        tfile.Tag.Pictures = new TagLib.IPicture[1] { pic };


        tfile.Save();
        */
    }
}
