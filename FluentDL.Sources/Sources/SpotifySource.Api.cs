using FluentDL.Core.Helpers;
using FluentDL.Core.Matching;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services.CustomSpotify;
using FluentDL.Services.Playlists;
using FluentDL.Contracts.Sources;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;


namespace FluentDL.Services.Sources
{
    internal sealed partial class SpotifySource
    {
        private static readonly SemaphoreSlim _lock = new(1, 1);
        private SpotifyClientConfig config = SpotifyClientConfig.CreateDefault().WithRetryHandler(new SimpleRetryHandler() { RetryTimes = 3, TooManyRequestsConsumesARetry = false});
        private SpotifyClient spotify;
        private SpotifyClient? spotifyUser;
        public bool IsInitialized = false;
        private string? loginString = null;
        
        // Login as user related
        private string? _clientId, _clientSecret;
        private static readonly int _port = 5543;
        private static readonly string _redirect = $"http://127.0.0.1:{_port}/callback";
        private readonly SemaphoreSlim userLoginGate = new(1, 1);
        private object authorizationGeneration = new();
        private SpotifyUserAuthorization? userAuthorization;
        private string? playlistUserId;
        private CancellationTokenSource? activeUserLogin;

        // Custom web player
        private ISpotifyWebService? spotifyWebService;
        private ISpotifyISRCService? spotifyISRCService;

        public static string GetCallbackUri()
        {
            return _redirect;
        }

        public async Task Initialize(string? clientId, string? clientSecret,  AuthenticationCallback? authCallback = null)
        {
            await _lock.WaitAsync();
            try 
            {
                IsInitialized = false;
                loginString = null;
                var keepUserSession = _clientId == clientId && _clientSecret == clientSecret && IsSignedInForPlaylists;
                if (!keepUserSession)
                {
                    authorizationGeneration = new();
                    activeUserLogin?.Cancel();
                    spotifyUser = null;
                    userAuthorization = null;
                    playlistUserId = null;
                }
                _clientId = clientId;
                _clientSecret = clientSecret;
                if (!keepUserSession) await RestoreUserAuthorizationAsync(clientId, clientSecret);
                
                try
                {
                    // First try to use clientId and clientSecret from settings
                    if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
                    {
                        // Save these for login usage (if user wants to login with browser, we need these to get the token)
                        _clientId = clientId;
                        _clientSecret = clientSecret;

                        // Create client without user access
                        spotify = new SpotifyClient(config.WithAuthenticator(new ClientCredentialsAuthenticator(clientId, clientSecret)));
                        if (await CheckClient())
                        {
                            IsInitialized = true;
                            spotifyWebService = null;
                            spotifyISRCService = null;
                            authCallback?.Invoke(StatusLevel.Success, "Using official Spotify API");
                            loginString = $"Using official API developer app:\nClient ID: {clientId}";
                            return;
                        }
                    }
                } catch (Exception ex)
                {
                    Debug.WriteLine("Failed to initialize spotify API: " +  ex.Message);
                }

                // Create web services if normal api not configured
                spotifyWebService ??= injectedWebService;
                spotifyISRCService ??= injectedIsrcService;

                // Check if spotify web player services are authenticated
                if (await spotifyWebService.IsAuthenticated(10000))
                {
                    IsInitialized = true;
                    authCallback?.Invoke(StatusLevel.Success, "Logged in with Spotify Web Player");
                    loginString = $"Logged into Spotify Web Player (anonymous)";
                    return;
                }
            } catch (Exception ex)
            {
                authCallback?.Invoke(StatusLevel.Error, "Failed to log into Spotify: " + ex.Message);
            } finally
            {
                _lock.Release();
            }
        }

        public Task<bool> AuthorizePlaylistAsync(CancellationToken token) => AuthorizeUserAsync(token);

        public bool IsSignedInForPlaylists => userAuthorization?.CanWritePlaylists == true && playlistUserId != null;
        public string? UserAccountId => userAuthorization != null ? playlistUserId : null;

        public async Task ClearUserAuthorizationAsync()
        {
            authorizationGeneration = new();
            activeUserLogin?.Cancel();
            userAuthorization = null;
            playlistUserId = null;
            spotifyUser = null;
            await userSessions.ClearAsync();
        }

        internal Func<string, string, string, CancellationToken, Task<AuthorizationCodeRefreshResponse>> RefreshUserTokenAsync { get; set; } =
            (id, secret, refresh, token) => new OAuthClient().RequestToken(new AuthorizationCodeRefreshRequest(id, secret, refresh), token);

        internal Func<SpotifyUserAuthorization, CancellationToken, Task<string>>? VerifyUserAccountAsync { get; set; }
        internal Func<Task<bool>>? CheckCatalogCredentialsAsync { get; set; }

        private async Task<string> VerifyAccountAsync(SpotifyUserAuthorization auth, CancellationToken token) =>
            VerifyUserAccountAsync != null ? await VerifyUserAccountAsync(auth, token)
                : (await new SpotifyClient(config.WithAuthenticator(auth)).UserProfile.Current(token)).Id;

        private Task SaveUserSessionAsync(AuthorizationCodeTokenResponse response, string clientId, string secret, string userId, object generation) =>
            userSessions.SaveAsync(new(clientId, SpotifyUserSessionStore.CredentialsHash(clientId, secret), userId, response.RefreshToken, response.Scope),
                () => ReferenceEquals(generation, authorizationGeneration));

        private SpotifyUserAuthorization CreateUserAuthorization(AuthorizationCodeTokenResponse response, string clientId, string secret, string userId, object generation) =>
            new(response, ct => RefreshUserTokenAsync(clientId, secret, response.RefreshToken, ct),
                updated => SaveUserSessionAsync(updated, clientId, secret, userId, generation),
                async () => { if (ReferenceEquals(generation, authorizationGeneration)) await ClearUserAuthorizationAsync(); });

        internal async Task RestoreUserAuthorizationAsync(string? clientId, string? secret)
        {
            var generation = authorizationGeneration;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
                {
                    await userSessions.ClearAsync();
                    return;
                }
                var saved = await userSessions.ReadAsync(clientId, secret, () => ReferenceEquals(generation, authorizationGeneration));
                if (saved == null) return;
                var refreshed = await RefreshUserTokenAsync(clientId, secret, saved.RefreshToken, deadline.Token);
                if (!ReferenceEquals(generation, authorizationGeneration)) return;
                var response = new AuthorizationCodeTokenResponse
                {
                    AccessToken = refreshed.AccessToken, RefreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken) ? saved.RefreshToken : refreshed.RefreshToken,
                    CreatedAt = refreshed.CreatedAt, ExpiresIn = refreshed.ExpiresIn, TokenType = refreshed.TokenType,
                    Scope = refreshed.Scope ?? saved.Scope,
                };
                if (!SpotifyUserAuthorization.HasPlaylistScopes(response.Scope) || string.IsNullOrWhiteSpace(response.AccessToken))
                {
                    await ClearUserAuthorizationAsync();
                    return;
                }
                // Persist rotation before profile lookup, so a transient profile failure cannot lose the new refresh token.
                await SaveUserSessionAsync(response, clientId, secret, saved.UserId, generation);
                var auth = CreateUserAuthorization(response, clientId, secret, saved.UserId, generation);
                var userId = await VerifyAccountAsync(auth, deadline.Token);
                if (!ReferenceEquals(generation, authorizationGeneration)) return;
                if (userId != saved.UserId)
                {
                    await ClearUserAuthorizationAsync();
                    return;
                }
                deadline.Token.ThrowIfCancellationRequested();
                spotifyUser = new SpotifyClient(config.WithAuthenticator(auth));
                playlistUserId = userId;
                userAuthorization = auth;
            }
            catch (APIException error) when (SpotifyUserAuthorization.IsRejected(error))
            {
                if (ReferenceEquals(generation, authorizationGeneration)) await ClearUserAuthorizationAsync();
            }
            catch (Exception error)
            {
                // Keep the encrypted session after offline, timeout or server failures. Never open a browser here.
                Serilog.Log.Warning("Could not restore Spotify user sign-in: {ExceptionType}", error.GetType().Name);
            }
        }

        private async Task<bool> AuthorizeUserAsync(CancellationToken token)
        {
            await userLoginGate.WaitAsync(token);
            try
            {
                if (spotifyUser != null && IsSignedInForPlaylists) return true;
                var clientId = _clientId;
                var secret = _clientSecret;
                var generation = authorizationGeneration;
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret)) return false;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                using var server = new EmbedIOAuthServer(new Uri(_redirect), _port);
                activeUserLogin = deadline;
                var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
                server.AuthorizationCodeReceived += (_, response) =>
                {
                    if (state == response.State && ReferenceEquals(generation, authorizationGeneration)) completion.TrySetResult(response.Code);
                    return Task.CompletedTask;
                };
                server.ErrorReceived += (_, _, receivedState) =>
                {
                    if (state == receivedState) completion.TrySetResult(null);
                    return Task.CompletedTask;
                };
                try
                {
                    await server.Start();
                    var request = new LoginRequest(new Uri(_redirect), clientId, LoginRequest.ResponseType.Code)
                    {
                        State = state,
                        Scope = SpotifyUserAuthorization.PlaylistScopes.Append(Scopes.PlaylistReadCollaborative).ToList(),
                    };
                    BrowserUtil.Open(request.ToUri());
                    var code = await completion.Task.WaitAsync(deadline.Token);
                    if (code == null) return false;
                    var oauth = new OAuthClient();
                    var response = await oauth.RequestToken(new AuthorizationCodeTokenRequest(clientId, secret, code, new Uri(_redirect)), deadline.Token);
                    var auth = new SpotifyUserAuthorization(response,
                        ct => RefreshUserTokenAsync(clientId, secret, response.RefreshToken, ct));
                    if (!auth.CanWritePlaylists) return false;
                    var userClient = new SpotifyClient(config.WithAuthenticator(auth));
                    var profile = await userClient.UserProfile.Current(deadline.Token);
                    deadline.Token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(generation, authorizationGeneration) || string.IsNullOrEmpty(profile.Id)) return false;
                    if (string.IsNullOrWhiteSpace(response.RefreshToken)) return false;
                    await SaveUserSessionAsync(response, clientId, secret, profile.Id, generation);
                    if (deadline.IsCancellationRequested && ReferenceEquals(generation, authorizationGeneration))
                        await ClearUserAuthorizationAsync();
                    deadline.Token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(generation, authorizationGeneration)) return false;
                    auth = CreateUserAuthorization(response, clientId, secret, profile.Id, generation);
                    spotifyUser = new SpotifyClient(config.WithAuthenticator(auth));
                    playlistUserId = profile.Id;
                    userAuthorization = auth;
                    return true;
                }
                finally
                {
                    if (ReferenceEquals(activeUserLogin, deadline)) activeUserLogin = null;
                    await server.Stop();
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                Serilog.Log.Warning("Spotify user authorization failed: {ExceptionType}", error.GetType().Name);
                return false;
            }
            finally { userLoginGate.Release(); }
        }

        private async Task<bool> CheckClient()
        {
            if (CheckCatalogCredentialsAsync != null) return await CheckCatalogCredentialsAsync();
            try
            {
                var responseTask = spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, "test"));
                var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(3)); // 3s timeout

                return response != null;
            }
            catch (TimeoutException)
            {
                Debug.WriteLine("Spotify CheckClient timed out");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Spotify CheckClient failed: {ex.Message}");
                return false;
            }
        }

        private bool CloseMatch(string str1, string str2)
        {
            return FuzzyMatch.IsSubstring(str1.ToLower(), str2.ToLower());
        }

        public string? LoginString()
        {
            return loginString;
        }

        // For whatever reason, a request like below throws bad request error
        // REQUEST: artist:"The Beep Test" track:"The Beep Test: 20 Metre (Complete Test)" album:"The Beep Test: The Best 20 Metre and 15 Metre Bleep Test for Personal Fitness & Recruitment Practice to the Police, RAF, Army, Fire Brigade, Royal Air Force, Royal Navy and the Emergency Services"
        public async Task AdvancedSearch(ICollection<SongSearchObject> itemSource, string artistName, string trackName, string albumName, CancellationToken token, int offset = 0, int limit = 25, bool clearResults = true)
        {
            // Trim
            artistName = artistName.Trim();
            trackName = trackName.Trim();
            albumName = albumName.Trim();
            if (!IsInitialized || (artistName.Length == 0 && trackName.Length == 0 && albumName.Length == 0)) // If no search query
            {
                return;
            }

            if (clearResults)
            {
                itemSource.Clear();
            }

            // The web player has no field search, so it runs a normal search for the fields joined. The official client
            // used below doesn't exist in that mode (bug 9).
            if (spotifyWebService != null)
            {
                var query = string.Join(" ", new[] { artistName, trackName, albumName }.Where(s => s.Length > 0));
                await GeneralSearch(itemSource, query, token, offset, limit);
                return;
            }

            var reqStr = "";
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                reqStr += $"artist:\"{artistName}\" ";
            }

            if (!string.IsNullOrWhiteSpace(trackName))
            {
                reqStr += $"track:\"{trackName}\" ";
            }

            if (!string.IsNullOrWhiteSpace(albumName))
            {
                reqStr += $"album:\"{albumName}\" ";
            }

            reqStr = reqStr.Trim(); // Trim the query

            SearchResponse? response;
            try
            {
                response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr)
                {
                    Offset = offset,
                    Limit = limit
                }, token);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to advanced search: " + e.Message);
                // Remove album: and everything after
                var albumIdx = reqStr.IndexOf("album:");
                if (albumIdx != -1)
                {
                    reqStr = reqStr.Substring(0, albumIdx);
                    response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr)
                    {
                        Offset = offset,
                        Limit = limit
                    }, token);
                }
                else
                {
                    return;
                }
            }

            if (response.Tracks == null || response.Tracks.Items == null)
            {
                return;
            }
            
            
            int count = 0;
            int totalCount = 0;
            await foreach (FullTrack track in spotify.Paginate(response.Tracks, (s) => s.Tracks)) {
                if (token.IsCancellationRequested || count >= limit || totalCount >= 100) return;
                var song = await Task.Run(() => ConvertSongSearchObject(track), token);
                if (song != null)
                {
                    itemSource.Add(song);
                    count++;  // Keep track of valid iterations
                }
                totalCount++;  // On every iteration
            }
        }

        public async Task GeneralSearch(ICollection<SongSearchObject> itemSource, string query, CancellationToken token, int offset = 0, int limit = 25, bool albumMode = false)
        {
            query = query.Trim(); // Trim the query
            //limit = Math.Min(limit, 1000);

            if (!IsInitialized || string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            // A web service was created
            if (spotifyWebService != null)
            {
                try
                {
                    await foreach (var song in spotifyWebService.SearchAnonymousAsync(query, offset, limit, token))
                    {
                        itemSource.Add(song);
                    }
                } catch { }
            }
            else
            {
                // Normal API
                SearchResponse? response = null;
                try
                {
                    response = await spotify.Search.Item(new SearchRequest(albumMode ? SearchRequest.Types.Album : SearchRequest.Types.Track, query)
                    {
                        Offset = offset,
                        Limit = limit
                    }, token);
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to general search: " + e.Message);
                    return; // If failed, return
                }

                if (response?.Tracks?.Items == null && response?.Albums?.Items == null)
                {
                    return;
                }

                try
                {
                    // Only the first page: Spotify already returns `limit` results from `offset`. Paginate used to walk
                    // every later page as well, loading hundreds of results for one search (bug 17).
                    if (albumMode)
                    {
                        foreach (var album in response.Albums?.Items ?? [])
                        {
                            if (token.IsCancellationRequested) return;
                            var albumObj = ConvertAlbumSearchObject(await spotify.Albums.Get(album.Id));

                            if (albumObj != null)
                            {
                                itemSource.Add(albumObj);
                            }
                        }
                    }
                    else
                    {
                        foreach (FullTrack track in response.Tracks?.Items ?? [])
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
                    Debug.WriteLine("Failed general search: " + e.ToString());
                }
            }
        }

        public async Task<string?> GetPlaylistName(string playlistId, CancellationToken token = default)
        {
            if (spotifyUser == null) return null;
            try
            {
                var playlist = await spotifyUser.Playlists.Get(playlistId, cancel: token);
                var playlistName = playlist.Name;
                return playlistName;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to get playlist name: " + e.Message);
                return null;
            }
        }

        public async Task<string?> GetIsrcFromId(string id)
        {
            try
            {
                if (spotifyISRCService == null)
                {
                    var track = await spotify.Tracks.Get(id);
                    return track.ExternalIds["isrc"];
                }
            } catch (Exception e)
            {
                Debug.WriteLine("Failed to get ISRC from id using official API: " + e.Message);
            }

            // Fallback
            try
            {
                var isrcServiceInstance = spotifyISRCService ?? injectedIsrcService;
                return await isrcServiceInstance.GetIsrc(id);
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to get ISRC from id: " + e.Message);
                return null;
            }
        }

        public async Task<SongSearchObject?> GetTrackFromISRC(string isrc, CancellationToken token = default)
        {
            if (spotifyWebService != null) // The web player has no ISRC search (bug 9)
            {
                return null;
            }

            try 
            {
                // https://api.spotify.com/v1/search?type=track&q=isrc:{isrc}
                var response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, $"isrc:{isrc}"), token);

                if (token.IsCancellationRequested)
                {
                    return null;
                }

                if (response.Tracks.Items == null)
                {
                    return null;
                }

                // Loop through and check if isrc matches
                foreach (var track in response.Tracks.Items)
                {
                    if (track.ExternalIds["isrc"] == isrc)
                    {
                        return ConvertSongSearchObject(track);
                    }
                }

            } catch (Exception e)
            {
                Debug.WriteLine("Failed to get track from ISRC: " + e.Message);
            }
            return null;
        }

        public string? GetSpotifyPreviewUrl(string html)
        {
            try
            {
                // Find occurrence of audio preview
                var previewStr = "\"audioPreview\":{\"url\":\"";
                var startIdx = html.IndexOf(previewStr);
                if (startIdx == -1)
                {
                    return null; // Not found
                }
                var endIdx = html.IndexOf("\"}", startIdx + previewStr.Length);
                var url = html.Substring(startIdx + previewStr.Length, endIdx - (startIdx + previewStr.Length));
                return url;
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to get spotify preview url: " + e.Message);
            }
            return null;
        }

        public async Task<SongSearchObject?> GetTrackWebPlayer(string id)
        {
            try
            {
                if (spotifyWebService == null)
                {
                    return null;
                }
                // First get from web service
                var track = await spotifyWebService.GetTrack(id);

                // Add additional/missing info from embed
                var httpClient = new System.Net.Http.HttpClient();
                string html = await httpClient.GetStringAsync($"https://open.spotify.com/embed/track/{id}");

                // get json
                var m = Regex.Match(html, @"<script[^>]*id=[""']__NEXT_DATA__[""'][^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase);
                var jsonStr = m.Success ? m.Groups[1].Value.Trim() : null;
                if (jsonStr == null)
                    return track;

                track ??= new SongSearchObject();
                using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;
                // Navigate to props.pageProps.state.data.entity
                if (!root.TryGetProperty("props", out var props) ||
                    !props.TryGetProperty("pageProps", out var pageProps) ||
                    !pageProps.TryGetProperty("state", out var state) ||
                    !state.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("entity", out var entity))
                {
                    return track;  // Return early
                }

                // Title / Name
                if (entity.TryGetProperty("name", out var nameProp) && string.IsNullOrEmpty(track.Title))
                    track.Title = nameProp.GetString() ?? string.Empty;
                else if (entity.TryGetProperty("title", out var titleProp) && string.IsNullOrEmpty(track.Title))
                    track.Title = titleProp.GetString() ?? string.Empty;
                // Id
                if (entity.TryGetProperty("id", out var idProp) && string.IsNullOrEmpty(track.Id))
                    track.Id = idProp.GetString() ?? string.Empty;
                else if (entity.TryGetProperty("uri", out var uriProp) && string.IsNullOrEmpty(track.Id))
                    track.Id = uriProp.GetString() ?? string.Empty;

                // Artists (join names)
                if (entity.TryGetProperty("artists", out var artistsProp) && artistsProp.ValueKind == JsonValueKind.Array && string.IsNullOrEmpty(track.Artists))
                {
                    var names = artistsProp.EnumerateArray()
                                          .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() : null)
                                          .Where(s => !string.IsNullOrEmpty(s))
                                          .ToArray();
                    track.Artists = string.Join(", ", names);
                }

                // Release date
                if (entity.TryGetProperty("releaseDate", out var releaseDateProp) &&
                    releaseDateProp.TryGetProperty("isoString", out var isoProp) &&
                    string.IsNullOrEmpty(track.ReleaseDate))
                {
                    var iso = isoProp.GetString();
                    if (DateTime.TryParse(iso, null, DateTimeStyles.AdjustToUniversal, out var dt))
                        track.ReleaseDate = dt.ToString("yyyy-MM-dd");
                    else
                        track.ReleaseDate = iso ?? string.Empty;
                }

                // Duration (ms -> mm:ss)
                if (entity.TryGetProperty("duration", out var durationProp) && durationProp.TryGetInt32(out var ms) && string.IsNullOrEmpty(track.Duration))
                {
                    var ts = TimeSpan.FromMilliseconds(ms);
                    track.Duration = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
                }

                // ImageLocation: pick largest available image or first
                if (entity.TryGetProperty("visualIdentity", out var visual) &&
                    visual.TryGetProperty("image", out var images) && images.ValueKind == JsonValueKind.Array
                    && string.IsNullOrEmpty(track.ImageLocation))
                {
                    // prefer largest by maxWidth if present
                    var best = images.EnumerateArray()
                                     .Select(i => new
                                     {
                                         Url = i.TryGetProperty("url", out var u) ? u.GetString() : null,
                                         Width = i.TryGetProperty("maxWidth", out var w) && w.TryGetInt32(out var wi) ? wi : 0
                                     })
                                     .Where(x => !string.IsNullOrEmpty(x.Url))
                                     .OrderByDescending(x => x.Width)
                                     .FirstOrDefault();

                    track.ImageLocation = best?.Url ?? track.ImageLocation;
                }
                else if (entity.TryGetProperty("image", out var imageProp) && imageProp.ValueKind == JsonValueKind.Array && string.IsNullOrEmpty(track.ImageLocation)
                    && string.IsNullOrEmpty(track.ImageLocation))
                {
                    var first = imageProp.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("url", out var u))
                        track.ImageLocation = u.GetString() ?? track.ImageLocation;
                }

                // Explicit flag
                if (entity.TryGetProperty("isExplicit", out var explicitProp) && explicitProp.ValueKind == JsonValueKind.True)
                    track.Explicit = true;
                else
                    track.Explicit = false;

                // AlbumName (if available)
                if (entity.TryGetProperty("album", out var albumProp) && albumProp.TryGetProperty("name", out var albumName) && string.IsNullOrEmpty(track.AlbumName))
                    track.AlbumName = albumName.GetString() ?? string.Empty;
                // Source and Rank and TrackPosition defaults
                track.Source = SourceIds.Spotify;

                // Embed str
                track.AdditionalFields ??= [];
                track.AdditionalFields["preview_url"] = GetSpotifyPreviewUrl(html);

                return track;
            }
            catch (Exception ex)
            {
                return null;
            }
        }
        public async Task<SongSearchObject?> GetTrack(string id)
        {
            try
            {
                if (spotifyWebService != null)
                {
                    return await GetTrackWebPlayer(id);
                }
                var track = await spotify.Tracks.Get(id);
                return ConvertSongSearchObject(track);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                return null;
            }
        }

        public async Task<FullAlbum?> GetAlbum(string id)
        {
            if (spotifyWebService != null) // The web player can't load albums (bug 9)
            {
                return null;
            }

            try
            {
                var album = await spotify.Albums.Get(id);
                return album;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                return null;
            }
        }

        public async Task AddTracksFromLink(ICollection<SongSearchObject> itemSource, string url, CancellationToken token, UrlStatusUpdateCallback? statusUpdate, bool albumMode = false)
        {
            if (!IsInitialized && spotifyUser == null)
            {
                throw new SourceSignInRequiredException(SourceIds.Spotify, "Spotify access is not configured. Configure Spotify in Settings.");
            }

            var id = url.Split("/").Last();
            // Remove any query parameters
            if (id.Contains("?"))
            {
                id = id.Split("?").First();
            }

            if (url.StartsWith("https://open.spotify.com/playlist/"))
            {
                ISpotifyWebService webServiceInstance = spotifyWebService ?? injectedWebService;
                var userLogin = spotifyUser != null && userAuthorization != null;
                var published = 0;

                statusUpdate?.Invoke(StatusLevel.Informational, $"<b>Spotify</b>   Loading <a href='{url}'>playlist</a>", -1);

                // Attempt official API (private or collaborative playlists only)
                try
                {
                    if (userLogin)
                    {
                        var playlistName = await GetPlaylistName(id, token);
                        if (playlistName == null)
                        {
                            throw new InvalidOperationException("Spotify could not read this playlist with the signed-in account.");
                        }

                        statusUpdate?.Invoke(StatusLevel.Informational, $"<b>Spotify</b>   Loading playlist <a href='{url}'>{playlistName}</a>", -1);

                        var pages = await spotifyUser!.Playlists.GetPlaylistItems(id, cancel: token);
                        await foreach (var item in spotifyUser.Paginate(pages, cancel: token))
                        {
                            if (item.Item is FullTrack track)
                            {
                                if (token.IsCancellationRequested)
                                {
                                    statusUpdate?.Invoke(StatusLevel.Warning, $"<b>Spotify</b>   Cancelled loading playlist <a href='{url}'>{playlistName}</a>");
                                    return; // Stop if cancelled
                                }

                                var songObj = ConvertSongSearchObject(track);
                                if (songObj != null)
                                {
                                    itemSource.Add(songObj);
                                    published++;
                                    if (published % 50 == 0) await Task.Delay(1, token);
                                }
                            }
                        }
                        statusUpdate?.Invoke(StatusLevel.Success, $"<b>Spotify</b>   Loaded playlist <a href='{url}'>{playlistName}</a>");
                    }
                    else
                    {
                        await foreach (var song in webServiceInstance.GetPlaylistAsync(id, token))
                        {
                            itemSource.Add(song);
                        }
                        statusUpdate?.Invoke(StatusLevel.Success, $"<b>Spotify</b>   Loaded <a href='{url}'>playlist</a>");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (SourceSignInRequiredException) { throw; }
                catch (Exception ex)
                {
                    if (published > 0)
                    {
                        statusUpdate?.Invoke(StatusLevel.Warning, $"<b>Spotify</b>   Loaded {published:N0} tracks, but could not finish the playlist. Results are incomplete. Try loading the link again.", 10);
                        return;
                    }
                    if (userLogin)  // Attempt webservice fallback
                    {
                        try
                        {
                            await foreach (var song in webServiceInstance.GetPlaylistAsync(id, token))
                            {
                                itemSource.Add(song);
                            }
                            statusUpdate?.Invoke(StatusLevel.Warning, $"<b>Spotify</b>   Error: {ex.Message}. Loaded <a href='{url}'>playlist</a> using fallback method.", 10);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (SourceSignInRequiredException) { throw; }
                        catch (Exception ex2)
                        {
                            statusUpdate?.Invoke(StatusLevel.Error, $"<b>Spotify</b>   Failed to load with both methods. Error 1: {ex.Message} | Error 2: {ex2.Message}", 10);
                        }
                    } else
                    {
                        throw new SourceSignInRequiredException(SourceIds.Spotify,
                            "Spotify could not read this playlist anonymously. If it is private, sign in through Settings and paste the link again.");
                    }
                }
            }

            if (url.StartsWith("https://open.spotify.com/album/"))
            {
                if (spotifyWebService != null) // The web player can't load albums (bug 9)
                {
                    statusUpdate?.Invoke(StatusLevel.Error, $"<b>Spotify</b>   Album links need a Spotify client ID and secret (Settings). Cannot load <a href='{url}'>{url}</a>");
                    return;
                }

                var album = await spotify.Albums.Get(id, token);
                
                if (albumMode)
                {
                    var albumObj = ConvertAlbumSearchObject(album);
                    if (albumObj != null)
                    {
                        itemSource.Add(albumObj);
                    }
                    return;
                }

                statusUpdate?.Invoke(StatusLevel.Informational, $"<b>Spotify</b>   Loading album <a href='{url}'>{album.Name}</a>", -1);

                var pages = album.Tracks;
                var allPages = await spotify.PaginateAll(pages, cancellationToken: token);

                foreach (var simpleTrack in allPages)
                {
                    if (token.IsCancellationRequested)
                    {
                        statusUpdate?.Invoke(StatusLevel.Warning, $"<b>Spotify</b>   Cancelled loading album <a href='{url}'>{album.Name}</a>");
                        return; // Stop if cancelled
                    }

                    // Get full track
                    var track = await spotify.Tracks.Get(simpleTrack.Id);
                    var songObj = await Task.Run(() => ConvertSongSearchObject(track), token);
                    if (songObj != null)
                    {
                        itemSource.Add(songObj);
                    }
                }

                statusUpdate?.Invoke(StatusLevel.Success, $"<b>Spotify</b>   Loaded album <a href='{url}'>{album.Name}</a>");
            }

            if (url.StartsWith("https://open.spotify.com/track/")) // Single track, no need to clear item source
            {
                try
                {
                    if (spotifyWebService != null)
                    {
                        var fullTrack = await GetTrackWebPlayer(id);
                        if (fullTrack != null)
                        {
                            itemSource.Add(fullTrack);
                            statusUpdate?.Invoke(StatusLevel.Success, $"<b>Spotify</b>   Loaded track <a href='{url}'>{fullTrack.Title}</a>");
                        }
                    }
                    else
                    {
                        var fullTrack = await spotify.Tracks.Get(id, token);
                        var songObj = ConvertSongSearchObject(fullTrack);
                        if (songObj != null)
                        {
                            itemSource.Add(songObj);
                            statusUpdate?.Invoke(StatusLevel.Success, $"<b>Spotify</b>   Loaded track <a href='{url}'>{fullTrack.Name}</a>");
                        }
                    }
                } catch (Exception ex)
                {
                    statusUpdate?.Invoke(StatusLevel.Error, $"<b>Spotify</b>   Error loading track: {ex.Message}");
                }
            }
        }

        // TODO: if 20 was not hit/searches are very low, then do not do a spotify filter search, instead append artist and track name as general query
        public async Task<SongSearchObject?> GetSpotifyTrack(SongSearchObject song, CancellationToken token = default, ConversionUpdateCallback? callback = null)
        {
            if (!IsInitialized)
            {
                callback?.Invoke(StatusLevel.Error, song);
                return null;
            }

            try
            {
                // Find by metadata
                var artistCSV = song.Artists;
                var artists = artistCSV.Split(", ");
                var trackName = song.Title;
                var albumName = song.AlbumName;


                var songObjList = new List<SongSearchObject>(); // List of SongSearchObject results
                HashSet<string> idSet = new(); // Set of track ids
                int LIMIT = 20;

                if (spotifyWebService != null)
                {
                    var reqStr = $"{trackName} {artists.FirstOrDefault() ?? ""}";
                    var observableList = new ObservableCollection<SongSearchObject>();
                    await GeneralSearch(observableList, reqStr, token, 0, LIMIT, false);
                    foreach (var songObj in observableList)
                    {
                        if (!idSet.Contains(songObj.Id))
                        {
                            idSet.Add(songObj.Id);
                            songObjList.Add(songObj);
                        }
                    }
                }
                else
                {
                    var reqStr = "";
                    if (!string.IsNullOrWhiteSpace(artistCSV))
                    {
                        reqStr += $"artist:\"{artistCSV}\" ";
                    }

                    if (!string.IsNullOrWhiteSpace(trackName))
                    {
                        reqStr += $"track:\"{trackName}\" ";
                    }

                    if (!string.IsNullOrWhiteSpace(albumName))
                    {
                        reqStr += $"album:\"{albumName}\" ";
                    }

                    reqStr = reqStr.Trim(); // Trim the query

                    // Try to find by ISRC first, then fall back to metadata (bugs 3 and 19: a failed ISRC search used to
                    // return null right away, without the metadata search or an error callback).
                    if (!string.IsNullOrWhiteSpace(song.Isrc))
                    {
                        var retObj = await GetTrackFromISRC(song.Isrc, token);

                        if (retObj != null) // Update callback with result
                        {
                            callback?.Invoke(StatusLevel.Success, retObj);
                            return retObj;
                        }
                    }


                    SearchResponse? response;
                    try
                    {
                        if (reqStr.Length >= 250)
                        {  // Maximum query is 250 characters
                            reqStr = reqStr.Substring(0, 250);
                        }
                        response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr), token);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Failed to advanced search: " + e.Message);
                        // Remove album: and everything after
                        var albumIdx = reqStr.IndexOf("album:");
                        if (albumIdx != -1)
                        {
                            reqStr = reqStr.Substring(0, albumIdx);
                            response = await spotify.Search.Item(new SearchRequest(SearchRequest.Types.Track, reqStr), token);
                        }
                        else
                        {
                            return null;
                        }
                    }

                    int notNullCount = 0, totalCount = 0;

                    await foreach (FullTrack track in spotify.Paginate(response.Tracks, (s) => s.Tracks))
                    {
                        if (token.IsCancellationRequested) return null;

                        if (notNullCount >= LIMIT || totalCount >= 1.5 * LIMIT) break;
                        totalCount++;  // Increment total count

                        var songObject = ConvertSongSearchObject(track);

                        if (songObject == null) continue;
                        notNullCount++;  // Increment count of non-null tracks found

                        if (!idSet.Contains(songObject.Id))
                        {
                            idSet.Add(songObject.Id);

                            var oneArtistMatch = artists.Any(a => track.Artists.Any(ta => CloseMatch(a, ta.Name)));

                            if (oneArtistMatch)
                            {
                                songObjList.Add(songObject);
                            }
                        }
                    }
                }

                // Pass 1: exact title match, find least edit distance album name
                SongSearchObject? closeMatchObj = null;
                int minEditDistance = int.MaxValue;
                foreach (var songObj in songObjList)
                {
                    if (token.IsCancellationRequested) // Cancel requested, terminate this method
                    {
                        return null;
                    }

                    var titlePruned = TitleNormalizer.CompactLegacy(trackName);
                    var songObjTitlePruned = TitleNormalizer.CompactLegacy(songObj.Title);
                    if (titlePruned.Equals(songObjTitlePruned) || titlePruned.Replace("radioedit", "").Equals(songObjTitlePruned.Replace("radioedit", ""))) // If the title matches without punctuation
                    {
                        if (albumName.ToLower().Replace(" ", "").Equals(songObj.AlbumName.ToLower().Replace(" ", ""))) // If the album name is exact match
                        {
                            callback?.Invoke(StatusLevel.Warning, songObj);
                            return songObj; // Return direct match
                        }

                        var dist = FuzzyMatch.LevenshteinDistance(TitleNormalizer.CompactLegacy(albumName), TitleNormalizer.CompactLegacy(songObj.AlbumName));
                        if (dist < minEditDistance)
                        {
                            minEditDistance = dist;
                            closeMatchObj = songObj;
                        }
                    }
                }

                if (closeMatchObj != null) // Return the least edit distance album name
                {
                    callback?.Invoke(StatusLevel.Warning, closeMatchObj);
                    return closeMatchObj;
                }
            } catch(Exception ex)
            {
                Debug.WriteLine("failed to get spotify track: " + ex.ToString());
            }

            callback?.Invoke(StatusLevel.Error, song); // Show error with original song object
            return null;
        }

        // NOTE: album images are 640, 300, then 64 
        public SongSearchObject? ConvertSongSearchObject(FullTrack track)
        {
            var artistCsv = track.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);

            if (artistCsv.Length == 0 || string.IsNullOrEmpty(track.Album.Name))
            {
                return null;
            }

            return new SongSearchObject
            {
                Source = SourceIds.Spotify,
                Title = track.Name,
                Artists = artistCsv,
                ImageLocation = track.Album.Images[1].Url, // Medium image, 300x300
                Id = track.Id,
                ReleaseDate = track.Album.ReleaseDate,
                Duration = ((int)Math.Round(track.DurationMs / 1000.0)).ToString(),
                Rank = "0",
                AlbumName = track.Album.Name,
                Explicit = track.Explicit,
                TrackPosition = track.TrackNumber.ToString(),
                Isrc = track.ExternalIds["isrc"],
            };
        }

        public SongSearchObject? ConvertSongSearchObject(SimpleTrack track, FullAlbum album)
        {
            var artistCsv = track.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);
            if (artistCsv.Length == 0 || string.IsNullOrEmpty(track.Name))
            {
                return null;
            }
            return new SongSearchObject
            {
                Source = SourceIds.Spotify,
                Title = track.Name,
                Artists = artistCsv,
                ImageLocation = album.Images[1].Url, // Medium image, 300 x 300
                Id = track.Id,
                Duration = ((int)Math.Round(track.DurationMs / 1000.0)).ToString(),
                ReleaseDate = "", // No release date available in SimpleTrack
                AlbumName = album?.Name ?? "",
                Explicit = track.Explicit,
                TrackPosition = track.TrackNumber.ToString(),
            };
        }

        public AlbumSearchObject? ConvertAlbumSearchObject(FullAlbum album)
        {
            var artistCsv = album.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);
            if (artistCsv.Length == 0 || string.IsNullOrEmpty(album.Name))
            {
                return null;
            }

            return new AlbumSearchObject
            {
                Source = SourceIds.Spotify,
                Title = album.Name,
                Artists = artistCsv,
                ImageLocation = album.Images[1].Url, // medium image, 300 x 300
                Id = album.Id,
                Duration = ((int)Math.Round(album.Tracks.Items?.Sum(t => t.DurationMs / 1000.0) ?? 0)).ToString(),
                ReleaseDate = album.ReleaseDate,
                AlbumName = album.Name,
                Explicit = album.Tracks.Items?.Any(t => t.Explicit) ?? false,
                TrackPosition = "1",
                Rank = "0",
                Isrc = album.ExternalIds.TryGetValue("upc", out var upc) ? upc : null,
                TracksCount = album.TotalTracks,
                TrackList = album.Tracks.Items?.Select(t => ConvertSongSearchObject(t, album))?.Where(t => t != null)?.ToList() ?? [],
                AdditionalFields = new Dictionary<string, object?> { { "artists", album.Artists}, { "cover_max", album.Images.First().Url} }
            };
        }

        public async Task UpdateMetadata(string filePath, string id)
        {
            if (!File.Exists(filePath))
            {
                return;
            }
            FullTrack? track = null;
            try
            {
                track = await spotify.Tracks.Get(id) ?? throw new Exception("Track not found");
                var fullAlbum = await spotify.Albums.Get(track.Album.Id); // Get the full album for genres + upc
                var artistList = track.Artists.Select(a => a.Name);
                var albumArtistList = fullAlbum.Artists.Select(a => a.Name);

                var metadata = new MetadataObject(filePath)
                {
                    Title = track.Name,
                    Artists = artistList.ToArray(),
                    AlbumName = track.Album.Name,
                    AlbumArtists = albumArtistList.ToArray(),
                    Isrc = track.ExternalIds["isrc"],
                    ReleaseDate = DateTime.Parse(track.Album.ReleaseDate),
                    AlbumArtPath = track.Album.Images.First().Url, // First is the largest image
                    TrackNumber = track.TrackNumber,
                    TrackTotal = track.Album.TotalTracks,
                    Upc = fullAlbum.ExternalIds["upc"],
                    Url = track.Uri,
                };

                await metadata.SaveAsync();
            }
            catch (Exception e) 
            {
                var songObj = await GetTrack(id);

                if (songObj == null)
                {
                    return;
                }

                var metadata = new MetadataObject(filePath)
                {
                    Title = songObj.Title,
                    Artists = songObj.Artists.Split(", "),
                    AlbumName = songObj.AlbumName,
                    AlbumArtists = songObj.Artists.Split(", "),
                    Isrc = songObj.Isrc,
                    ReleaseDate = DateTime.TryParse(songObj.ReleaseDate, out var dt) ? dt : null,
                    AlbumArtPath = songObj.ImageLocation,
                    TrackNumber = int.TryParse(songObj.TrackPosition, out var tn) ? tn : null,
                    Url = SourceCatalog.UrlOf(songObj),
                };

                await metadata.SaveAsync();
            }
        }
    }
}
