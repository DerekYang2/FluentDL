using FluentDL.Core.Helpers;
using FluentDL.Core.Matching;
using FluentDL.Core.Sources;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Services.CustomSpotify;
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
        private Random rand = new();
        public bool IsInitialized = false;
        private string? loginString = null;
        
        // Login as user related
        private EmbedIOAuthServer? _server;
        private string? _clientId, _clientSecret;
        private static readonly int _port = 5543;
        private static readonly string _redirect = $"http://127.0.0.1:{_port}/callback";
        private TaskCompletionSource<bool> _loginTcs = new();

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
                bool attemptedCustomIdSecret = false;
                loginString = null;
                
                try
                {
                    // First try to use clientId and clientSecret from settings
                    if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
                    {
                        // Save these for login usage (if user wants to login with browser, we need these to get the token)
                        _clientId = clientId;
                        _clientSecret = clientSecret;
                        _server = null;
                        spotifyUser = null;

                        // Create client without user access
                        attemptedCustomIdSecret = true;
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

                //// If clientId and clientSecret are not provided, try to get from browser
                //try
                //{
                //    spotify = new SpotifyClient(config.WithAuthenticator(new EmbedAuthenticator()));
                //    IsInitialized = true;
                //    return;
                //}
                //catch (Exception e)
                //{
                //    Debug.WriteLine("Failed to get access token: " + e.Message);
                //}

                //// If still not initialized, try to get bundled keys
                //try
                //{
                //    var idList = KeyReader.GetValues("spot_id");
                //    var secretList = KeyReader.GetValues("spot_secret");

                //    if (!idList.IsEmpty && !secretList.IsEmpty && idList.Length == secretList.Length)  // If lists have content and same length
                //    {
                //        // Shuffle idList
                //        var pairs = new List<(string? id, string? secret)>();
                //        for (int i = 0; i < idList.Length; i++)
                //        {
                //            pairs.Add((idList[i], secretList[i]));
                //        }
                //        var pairArr = pairs.ToArray();
                //        rand.Shuffle(pairArr);

                //        foreach (var (id, secret) in pairArr)
                //        {                        
                //            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(secret))
                //            {
                //                spotify = new SpotifyClient(config.WithAuthenticator(new ClientCredentialsAuthenticator(id, secret)));
                //                if (await CheckClient())
                //                {
                //                    IsInitialized = true;
                //                    if (attemptedCustomIdSecret)
                //                    {
                //                        authCallback?.Invoke(StatusLevel.Warning, "User credentials failed — using included instead");
                //                    }
                //                    else
                //                    {
                //                        authCallback?.Invoke(StatusLevel.Success, "Logged in with included credentials");
                //                    }
                //                    loginString = $"Logged in with included (FluentDL) credentials:\nSelected Client ID: {id[..5] + "..."}";
                //                    return;
                //                } else
                //                {
                //                    Debug.WriteLine("Failed to validate client: " + id);
                //                }
                //            }   
                //        }
                //    }
                //}
                //catch (Exception e)
                //{
                //    Debug.WriteLine("Failed to initialize Spotify API: " + e.Message);
                //    authCallback?.Invoke(StatusLevel.Error, "Failed to initialize: " + e.Message);
                //}
                //if (!IsInitialized)
                //{
                //    authCallback?.Invoke(StatusLevel.Error, "All credential options failed");
                //}
            } catch (Exception ex)
            {
                authCallback?.Invoke(StatusLevel.Error, "Failed to log into Spotify: " + ex.Message);
            } finally
            {
                _lock.Release();
            }
        }

        private async Task PromptUserLogin()
        {
            if (_clientId == null || _clientSecret == null)
            {
                Debug.WriteLine("Client ID or secret not set, cannot prompt user login");
                return;
            }

            if (_server == null)
            {
                _server = new EmbedIOAuthServer(new Uri(_redirect), _port);
                await _server.Start();

                _server.AuthorizationCodeReceived += OnAuthorizationCodeReceived;
                _server.ErrorReceived += OnErrorReceived;
            }

            if (spotifyUser == null)
            {
                var request = new LoginRequest(_server.BaseUri, _clientId, LoginRequest.ResponseType.Code)
                {
                    Scope = new List<string> { Scopes.PlaylistReadCollaborative, Scopes.PlaylistReadPrivate }
                };

                BrowserUtil.Open(request.ToUri());
                _loginTcs = new TaskCompletionSource<bool>();
            }
        }

        private async Task OnAuthorizationCodeReceived(object sender, AuthorizationCodeResponse response)
        {
            if (spotifyUser == null)
            {
                try
                {
                    var tokenResponse = await new OAuthClient().RequestToken(
                        new AuthorizationCodeTokenRequest(_clientId!, _clientSecret!, response.Code, new Uri(_redirect))
                    );
                    var customConfig = config.WithAuthenticator(new AuthorizationCodeAuthenticator(_clientId!, _clientSecret!, tokenResponse));
                    spotifyUser = new SpotifyClient(customConfig);
                    await _server!.Stop();
                    _loginTcs.TrySetResult(true);
                }
                catch (Exception ex) {
                    Debug.Write("Failed to authenticate spotify user: " + ex.ToString());
                    spotifyUser = null;
                    _loginTcs.TrySetResult(false);
                }
            }
        }

        private async Task OnErrorReceived(object sender, string error, string? state)
        {
            Debug.WriteLine($"Aborting authorization, error received: {error}");
            await _server!.Stop();
        }

        private async Task<bool> CheckClient()
        {
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
                    if (albumMode)
                    {
                        await foreach (var album in spotify.Paginate(response.Albums, s => s.Albums))
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
                        await foreach (FullTrack track in spotify.Paginate(response.Tracks, s => s.Tracks))
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

        public async Task<string?> GetPlaylistName(string playlistId)
        {
            if (spotifyUser == null) return null;
            try
            {
                var playlist = await spotifyUser.Playlists.Get(playlistId);
                var playlistName = playlist.Name;
                return playlistName;
            }
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

        public async Task<SongSearchObject?> GetTrackEmbed(string id)
        {
            var httpClient = new System.Net.Http.HttpClient();
            string html = await httpClient.GetStringAsync($"https://open.spotify.com/embed/track/{id}");
            // get json
            var m = Regex.Match(html, @"<script[^>]*id=[""']__NEXT_DATA__[""'][^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase);
            var jsonStr = m.Success ? m.Groups[1].Value.Trim() : null;
            if (jsonStr == null) 
                return null;

            using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;
            // Navigate to props.pageProps.state.data.entity
            if (!root.TryGetProperty("props", out var props) ||
                !props.TryGetProperty("pageProps", out var pageProps) ||
                !pageProps.TryGetProperty("state", out var state) ||
                !state.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("entity", out var entity))
            {
                throw new InvalidOperationException("Expected path props.pageProps.state.data.entity not found.");
            }

            var result = new SongSearchObject();

            // Title / Name
            if (entity.TryGetProperty("name", out var nameProp))
                result.Title = nameProp.GetString() ?? string.Empty;
            else if (entity.TryGetProperty("title", out var titleProp))
                result.Title = titleProp.GetString() ?? string.Empty;

            // Id
            if (entity.TryGetProperty("id", out var idProp))
                result.Id = idProp.GetString() ?? string.Empty;
            else if (entity.TryGetProperty("uri", out var uriProp))
                result.Id = uriProp.GetString() ?? string.Empty;

            // Artists (join names)
            if (entity.TryGetProperty("artists", out var artistsProp) && artistsProp.ValueKind == JsonValueKind.Array)
            {
                var names = artistsProp.EnumerateArray()
                                      .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() : null)
                                      .Where(s => !string.IsNullOrEmpty(s))
                                      .ToArray();
                result.Artists = string.Join(", ", names);
            }

            // Release date
            if (entity.TryGetProperty("releaseDate", out var releaseDateProp) &&
                releaseDateProp.TryGetProperty("isoString", out var isoProp))
            {
                var iso = isoProp.GetString();
                if (DateTime.TryParse(iso, null, DateTimeStyles.AdjustToUniversal, out var dt))
                    result.ReleaseDate = dt.ToString("yyyy-MM-dd");
                else
                    result.ReleaseDate = iso ?? string.Empty;
            }

            // Duration (ms -> mm:ss)
            if (entity.TryGetProperty("duration", out var durationProp) && durationProp.TryGetInt32(out var ms))
            {
                var ts = TimeSpan.FromMilliseconds(ms);
                result.Duration = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
            }

            // ImageLocation: pick largest available image or first
            if (entity.TryGetProperty("visualIdentity", out var visual) &&
                visual.TryGetProperty("image", out var images) && images.ValueKind == JsonValueKind.Array)
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

                result.ImageLocation = best?.Url ?? result.ImageLocation;
            }
            else if (entity.TryGetProperty("image", out var imageProp) && imageProp.ValueKind == JsonValueKind.Array)
            {
                var first = imageProp.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("url", out var u))
                    result.ImageLocation = u.GetString() ?? result.ImageLocation;
            }

            // Explicit flag
            if (entity.TryGetProperty("isExplicit", out var explicitProp) && explicitProp.ValueKind == JsonValueKind.True)
                result.Explicit = true;
            else
                result.Explicit = false;

            // AlbumName (if available)
            if (entity.TryGetProperty("album", out var albumProp) && albumProp.TryGetProperty("name", out var albumName))
                result.AlbumName = albumName.GetString() ?? string.Empty;

            // Source and Rank and TrackPosition defaults
            result.Source = SourceIds.Spotify;
            result.Rank = string.Empty;
            result.TrackPosition = "1";

            return result;
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

        // TODO: handle invalid playlist ids
        public async Task<List<SongSearchObject>> GetPlaylist(string playlistId, CancellationToken token)
        {
            var pages = await spotify.Playlists.GetItems(playlistId, cancel: token);
            var allPages = await spotify.PaginateAll(pages, cancellationToken: token);

            var songs = new List<SongSearchObject>();
            // Debug: loop and print all tracks
            foreach (PlaylistTrack<IPlayableItem> item in allPages)
            {
                if (item.Track is FullTrack track)
                {
                    if (token.IsCancellationRequested)
                    {
                        break; // Stop if cancelled
                    }

                    // All FullTrack properties are available
                    var artistCsv = track.Artists.Select(a => a.Name).Aggregate((a, b) => a + ", " + b);
                    if (artistCsv.Length == 0 || track.Album.Name.Length == 0)
                    {
                        continue;
                    }

                    var songObj = ConvertSongSearchObject(track);
                    if (songObj != null)
                    {
                        songs.Add(songObj);
                    }
                }
            }

            return songs;
        }

        public async Task AddTracksFromLink(ICollection<SongSearchObject> itemSource, string url, CancellationToken token, UrlStatusUpdateCallback? statusUpdate, bool userLogin, bool albumMode = false)
        {
            if (!IsInitialized)
            {
                statusUpdate?.Invoke(StatusLevel.Error, "Spotify API not initialized. Go to settings to authenticate.");
                return;
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

                statusUpdate?.Invoke(StatusLevel.Informational, $"<b>Spotify</b>   Loading <a href='{url}'>playlist</a>", -1);

                // Attempt official API (private or collaborative playlists only)
                try
                {
                    if (userLogin)
                    {
                        await PromptUserLogin();
                        var status = await _loginTcs.Task.WaitAsync(TimeSpan.FromSeconds(60)); // Wait for user to login, with timeout

                        if (!status || spotifyUser == null)
                        {
                            statusUpdate?.Invoke(StatusLevel.Error, $"<b>Spotify</b>   Authentication failed. Cannot load playlist <a href='{url}'>{url}</a>");
                            return;
                        }

                        var playlistName = await GetPlaylistName(id);
                        if (playlistName == null)
                        {
                            statusUpdate?.Invoke(StatusLevel.Error, $"<b>Spotify</b>   Failed to load playlist <a href='{url}'>{url}</a>");
                            return;
                        }

                        statusUpdate?.Invoke(StatusLevel.Informational, $"<b>Spotify</b>   Loading playlist <a href='{url}'>{playlistName}</a>", -1);

                        var pages = await spotifyUser.Playlists.GetPlaylistItems(id, cancel: token);
                        var allPages = await spotifyUser.PaginateAll(pages, cancellationToken: token);

                        // Debug: loop and print all tracks
                        foreach (PlaylistTrack<IPlayableItem> item in allPages)
                        {
                            if (item.Item is FullTrack track)
                            {
                                if (token.IsCancellationRequested)
                                {
                                    statusUpdate?.Invoke(StatusLevel.Warning, $"<b>Spotify</b>   Cancelled loading playlist <a href='{url}'>{playlistName}</a>");
                                    return; // Stop if cancelled
                                }

                                var songObj = await Task.Run(() => ConvertSongSearchObject(track), token);
                                if (songObj != null)
                                {
                                    itemSource.Add(songObj);
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
                catch (Exception ex)
                {
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
                        catch (Exception ex2)
                        {
                            statusUpdate?.Invoke(StatusLevel.Error, $"<b>Spotify</b>   Failed to load with both methods. Error 1: {ex.Message} | Error 2: {ex2.Message}", 10);
                        }
                    } else
                    {
                        statusUpdate?.Invoke(StatusLevel.Error, $"<b>Spotify</b>   Failed to load playlist: {ex.Message}", 10);
                    }
                }
            }

            if (url.StartsWith("https://open.spotify.com/album/"))
            {
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

                    // Try to find by ISRC first
                    if (song.Isrc != null)
                    {
                        var retObj = await GetTrackFromISRC(song.Isrc, token);

                        if (retObj != null) // Update callback with result
                        {
                            callback?.Invoke(StatusLevel.Success, retObj);
                        }

                        return retObj;
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
