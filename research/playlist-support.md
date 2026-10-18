# Playlist support research

Initial research on 16 October 2026 against `origin/master` at `1e92851`. Updated through 18 October with production writers for TIDAL, Deezer, Qobuz and Spotify. Queue Upload can create a playlist or append to an existing owned playlist. It saves only individual tracks for the selected service, skips duplicate catalog IDs, excludes albums and never converts tracks or changes the queue. TIDAL, Deezer and Qobuz passed disposable-playlist tests with the existing sign-ins. Spotify has simulated official-API coverage; its real sign-in/restart and resumed large upload still need user confirmation. TIDAL catalog reads now prefer its download session too. YouTube writes remain research only.

## Summary

FluentDL imports playlists by link and matches tracks between services. `IPlaylistWriter` adds owned-playlist listing, new-playlist creation and append-only updates for TIDAL, Deezer, Qobuz and Spotify. Renaming, deleting, reordering and removing existing entries remain out of scope.

| Destination | Existing dependency or API | Can reuse the current sign-in? | Work needed |
| --- | --- | --- | --- |
| Deezer | Web gateway, with DeezNET 1.2.2 session credentials | Yes, ARL and existing gateway session, live-tested | Implemented without DeezNET's automatic mutation retries |
| Qobuz | QobuzApiSharp 0.0.8 only exposes get and search; direct HTTP writes | Yes, existing app ID and user token, live-tested | Implemented with sequential batches and ordered read-back |
| Spotify | Official Web API create and item-write endpoints, shared OAuth session with SpotifyAPI.Web 7.4.2 | Explicit Settings authorization with playlist scopes | Implemented with DPAPI-saved refresh tokens, ordered verification and no mutation replay |
| TIDAL | Both official and unofficial account APIs passed live playlist tests | Yes, the existing download token worked with both APIs | Share download session/refresh logic with a playlist client; no second login was needed in this test |
| YouTube | Current YouTubeMusicAPI 3.0.11 and YoutubeExplode 6.6.2 don't expose playlist creation | No Google account session in FluentDL | Add official YouTube Data API OAuth or implement unsupported web endpoints |
| Local files | No remote API needed | No sign-in | Export an ordered UTF-8 M3U8 file of local paths; these files reference audio, they don't contain it |

## Deezer

`FluentDL.Sources/Sources/DeezerSource.Api.cs:29` already calls `SetARL` and `GWApi.GetUserData`. The exact source commit recorded in the installed DeezNET 1.2.2 package is `711cdb99894d7f130c7420875576f3b4776f48ac`.

Its `DeezNET/GWApi.cs` contains:

- `CreatePlaylist(title, description, trackIds, status, token)` at line 119.
- `EditPlaylist(playlistId, title, status, description, trackIds, token)` at line 127.
- `AddSongsToPlaylist(playlistId, trackIds, offset, token)` at line 136.
- `RemoveSongsFromPlaylist(playlistId, trackIds)` at line 145.
- `DeletePlaylist(playlistId, token)` at line 153.
- `GetPlaylistPage`, `GetPlaylistTracks`, and `GetUserProfilePage` for account/private reads through the gateway. `PublicApi.GetUserPlaylist` also lists a user's playlists, but public reads alone shouldn't be assumed to include private playlists.
- Favorites methods for songs, albums and playlists, if wanted later.

`PlaylistStatus` supports public, private and collaborative playlists. Creation defaults to public, so explicitly choose the intended visibility.

These use Deezer's unofficial `gw-light.php` web gateway, not a new official OAuth integration. They return raw JSON. Check IDs and error responses rather than treating a completed task as success. Playlist creation accepts the initial track IDs, which is useful for a first version of queue export.

Library cautions: `RemoveSongsFromPlaylist` does not accept cancellation, and its single-song wrapper ignores its cancellation argument. Gateway token errors can recursively refresh and retry without an explicit retry limit. Use bounded operation timeouts and don't blindly retry creates or appends after ambiguous failures. Verify edit/reordering semantics before using `EditPlaylist` to replace an existing playlist's contents.

### Implemented writer and live results

`DeezerPlaylistClient` reuses the active ARL, SID and gateway form token in `DeezerSource`. Its HTTP handler disables redirects and cookie storage. It sends the current session cookies explicitly and never refreshes or retries a mutation. A sign-out or account change stops the next batch.

- Private is the default. Public is the only other supported visibility. The writer verifies the created playlist's visibility before adding tracks.
- Creation returns a numeric ID. Appending returns a boolean, which alone cannot confirm the tracks. Each append therefore reads the playlist back through the account gateway and compares the full ordered IDs.
- A 51-track production-writer test passed across two batches. `offset=-1` appends at the end. Supplying the existing track count as an offset failed on the second batch and was not retried.
- The gateway rejected duplicate IDs within one request and rejected a separate append of an existing ID. The user subsequently chose an always-skip policy for all services. The writer now skips repeated queue IDs and IDs already present in an existing target, and reports their count. It never removes existing duplicate entries.
- The gateway silently truncated names at 50 characters. New-playlist input and writer validation enforce that limit. Existing appends never send name, description or visibility changes.
- The signed-in profile's playlist page included the newly created private playlist. Public playlist reads were not reliable for private-playlist cleanup checks.
- Cleanup returned true. Subsequent reads of each created playlist returned `PERMISSION_ERROR`, as expected after deletion. Every diagnostic and production test playlist was removed. No pre-existing playlist was changed.

## Qobuz

The installed QobuzApiSharp 0.0.8 XML documentation has `GetPlaylist` and `SearchPlaylists`, but no playlist writes. Upstream's playlist service also only implements these reads.

FluentDL already has the HTTP setup needed in `FluentDL.Sources/Sources/QobuzSource.Http.cs:27`: `X-App-Id` and `X-User-Auth-Token` headers on the Qobuz API at `https://www.qobuz.com/api.json/0.2`. `QobuzSource.Api.cs:37` obtains this session using the existing user ID and token, including the optional saved app ID and secret.

Music Assistant's Qobuz provider implements the following against that same API:

- `playlist/getUserPlaylists` for the user's playlist library.
- `playlist/create` with name, description, public and collaborative flags.
- `playlist/addTracks` with a playlist ID and comma-separated catalog track IDs.
- `playlist/deleteTracks` with playlist-entry IDs.
- `playlist/delete` for a playlist the account can edit.

Its create and item-write calls use its normal token-authenticated request helper without the optional request-signing mode. FluentDL's existing token also passed live create, append, read and delete tests without a second login or signed mutation requests.

An independent Python client implements the same create/add/delete operations. It batches add/remove requests in groups of 50 to limit URL length. Treat that as implementation guidance, not a verified server maximum.

Qobuz's published v0.2 documentation also describes `playlist/update` and `playlist/updateTracksPosition`. Those are candidates for rename, visibility changes and ordering, but need current request validation before implementation.

Important: removing a track uses `playlist_track_id`, not the catalog track ID. Store a separate playlist-entry ID if editing existing playlists is in scope. A list of `SongSearchObject` alone is insufficient.

### Implemented writer and live results

`QobuzPlaylistClient` uses the active app ID and user token from `QobuzSource`. Credentials remain in request headers. Readiness also requires a successful current login, so stale HTTP headers after sign-out cannot enable playlist writes.

- Private is the default, using `is_public=0` and `is_collaborative=0`. Public is the other supported visibility. The writer checks the create response's visibility before appending.
- Creation returns a playlist object with a numeric ID. `playlist/addTracks` returns a playlist object and `tracks_count`. The writer confirms the ordered contents using `playlist/get?extra=track_ids` after every batch instead of trusting that count alone.
- Qobuz removed repeated IDs within a single request even with `no_duplicate=0`. Separate requests could preserve repeated occurrences, but the user subsequently chose always-skip behavior. The writer now deduplicates the snapshot, excludes IDs already in an existing target, sends `no_duplicate=1` and reports the skipped count. Batches contain at most 50 IDs in first-occurrence queue order.
- The production writer passed a 51-track test across two batches and a three-track test with a repeated ID across two requests. Both tests verified exact order and private visibility through the existing source's playlist reader.
- Deletion returned success and each subsequent read returned HTTP 404. All test playlists were removed.

## Spotify

The installed SpotifyAPI.Web 7.4.2 source commit is `8648117e852e705b4876e288d9b5414f5c7aa451`. It already handles the February 2026 endpoint changes:

- `Playlists.Create(PlaylistCreateRequest)` posts to `/me/playlists`.
- `CurrentUsers` lists the user's playlists.
- `AddPlaylistItems`, `RemovePlaylistItems`, `ReplacePlaylistItems` and `UpdatePlaylistItems` use `/playlists/{id}/items`.
- `ChangeDetails` updates a playlist's name, description and visibility.
- `UploadCover` supports playlist artwork with the necessary additional authorization.

Do not copy older examples using `Create(userId, ...)` or `AddItems(...)`. Those methods still exist but are obsolete and target the old routes. The February changes concern Development Mode; don't assume identical restrictions for every extended-access integration.

Current FluentDL authentication in `FluentDL.Sources/Sources/SpotifySource.Api.cs`:

- Anonymous web-player access is not a signed-in user session. `SpotifyWebService.cs:12` only exposes search, track and playlist reads. It cannot write into an account.
- The client ID/secret session at line 63 uses client credentials, which also cannot manage a user's playlists.
- Explicit Settings authorization creates the shared user session and requests private/collaborative reads plus private/public playlist writes.

Playlist writes use `playlist-modify-private` and `playlist-modify-public`. For artwork, request `ugc-image-upload` only if that feature is implemented. A create operation produces an empty playlist; adding tracks is separate, with at most 100 URIs per request and input order preserved.

Access tokens live in memory. The refresh token, granted scopes and account/client binding now use DPAPI-encrypted storage. Startup restores only after refreshing under the same credentials and checking the same account. Cancellation, login failure completion and browser-server cleanup are implemented. Playlist writes do not depend on the removed private-playlist-read toggle. See `tools/live-tests/Spotify-playlists.md` for current behavior.

Current Development Mode restrictions matter for distribution: Spotify documents a Premium requirement for the developer app owner, a five-user allowlist for new apps, and limited access to playlist contents. The existing bring-your-own-client-ID setup fits personal use better than promising one shared Spotify app will work for every FluentDL user. Verify account eligibility during testing.

## TIDAL

The official API reference's OpenAPI document exposes third-party operations for:

- `POST /playlists`, create a playlist.
- `PATCH /playlists/{id}`, rename, describe and change visibility.
- `DELETE /playlists/{id}`, delete.
- `POST`, `DELETE` and `PATCH /playlists/{id}/relationships/items`, add, remove and reorder entries.
- `GET /userCollectionPlaylists/me/relationships/items`, browse the user's collection.

The specification marks these operations `THIRD_PARTY`. It lists `playlists.write` for writes, `playlists.read` for listing playlists, and `collection.read` for browsing My Collection. Legacy `r_usr` and `w_usr` scopes also appear, but the spec marks those internal. Use the documented third-party scopes and check the developer application's grants.

`FluentDL.Sources/Tidal/TidalApi.cs:46` currently asks only for `user.read`. Its official account OAuth code supports PKCE and encrypted saved refresh tokens, but `AccountLoginEnabled` at line 99 defaults to false. Catalog client-credentials access isn't enough to write into an account. Enable the account flow and obtain fresh consent for the new scopes.

`TidalDownloadClient` has a separate login for downloads. Do not assume that its session or internal `w_usr` scope can be used interchangeably with the official API's playlist authorization.

### Reusing the download login

The download login is OAuth too, but under a different client ID and redirect URI from FluentDL's own developer app. `TidalDownloadClient.cs:17` requests `r_usr+w_usr+w_sub`; the saved session includes an access token, refresh token, country and user ID. It already authenticates non-download requests, such as subscription lookup. It is not inherently limited to audio downloads.

The unofficial python-tidal client manages playlists with its account session. Its user code lists playlists under `users/{id}/playlists` and creates them through `api.tidal.com/v2/my-collection/playlists/folders/create-playlist`. Its playlist code adds tracks through `api.tidal.com/v1/playlists/{id}/items`, and implements rename, removal and deletion too.

This motivated testing the existing download token before requiring a second login. The 17 October tests below confirmed that the current test session's client can perform these operations. Never display or log the token.

Keep the original client ID paired with the refresh token. Do not transplant the refresh token into FluentDL's official OAuth client or assume acceptance by `openapi.tidal.com/v2`. If the unofficial route works, separate playlist request code can share a narrowly scoped authenticated-request/session provider with downloads, rather than exposing raw tokens to the UI.

The catalog API helper currently sends GET requests only. Add authenticated write handling, with permission errors and cancellation surfaced to the caller.

Current schema details:

- Create visibility is `PUBLIC` or `UNLISTED`, not a generic private/public boolean.
- Appends accept at most 50 items per request.
- Reordering accepts at most 20 entries per request and uses entry metadata.
- Appends have `onDuplicates` options `ADD`, `FAIL` and `SKIP`.
- Mutation endpoints document idempotency keys. Use them where supported to avoid duplicate mutations after uncertain network failures.

These documented capabilities were tested with the download session, not with FluentDL's own official OAuth client. Request limits and larger exports still need boundary tests.

### Live results, 17 October 2026

`tools/live-tests/TidalPlaylistProbe.cs` refreshed the existing download test session under its original client ID. The token response reported `r_usr`, `w_usr` and `w_sub`. Only that explicitly supplied encrypted test file was updated; the app's settings were not changed.

| Operation | Unofficial account API | Official API using the same download token |
| --- | --- | --- |
| List playlists/collection | 200 | 200 |
| Read account | Not needed after refresh returned the user ID | 200 |
| Create playlist | 200 | 201 |
| Verify unlisted visibility | Not checked | 200, `accessType=UNLISTED` |
| Append three tracks, including one repeated track | 200 | 200 |
| Read back exact order and duplicate | Passed | Passed through the official relationship endpoint |
| Delete test playlist | 204 | 200 |
| Verify deletion | 404 | 404 |

An initial unofficial append returned 412 without an ETag. Fetching the playlist and sending its current ETag as `If-None-Match`, as python-tidal does, fixed it. The initial test playlist was also deleted and its deletion verified. All five playlists created across the diagnostic runs were deleted; no pre-existing playlist was changed.

Recommendation after testing: prefer the official playlist API with the existing download session. Keep the unofficial implementation as a code-only alternative if desired, not an automatic retry after uncertain writes. Both use the same account session; neither requires enabling FluentDL's separate official OAuth login for this tested client. A different client ID or a TIDAL permission change can invalidate that compatibility.

The catalog integration is now implemented and tested through the actual `TidalSource`, not just diagnostic URLs. With app credentials disabled, the download account passed track and album searches, cursor pagination, advanced search, track/album metadata, covers, preview manifests, album and playlist pagination, and ISRC/UPC lookups. Offline tests cover account preference, shared refresh and rotation, sign-in/out changes, revoked sessions, official API token rejection, permissions errors, rate limits and cancellation. Signed-out catalog access still uses the existing developer-credentials path. No claim is made that user tokens have independent rate-limit quotas.

Calling the official API with an existing token does not convert its OAuth session to FluentDL's client. Refresh it only with the original client ID. If acceptance is revoked, a separate official authorization is still an option, but it must obtain its own tokens and consent.

## YouTube

`FluentDL.Sources/Sources/YoutubeSource.cs:20` creates anonymous YouTubeMusicAPI and YoutubeExplode clients. There is no Google account session. The installed YouTubeMusicAPI has library-reading methods with cookie authentication, but no public create/edit/delete playlist methods. YoutubeExplode and the other download libraries don't provide an account playlist writer either.

An official implementation would use YouTube Data API v3:

- OAuth with a YouTube write scope, not just an API key.
- `playlists.insert` to create, then `playlistItems.insert` to add each video.
- Playlist and item update/delete methods for editing later.
- Google Cloud project and consent configuration, token storage, quota handling and any verification needed for distribution.

The official docs currently charge 50 units for creating a playlist and 50 for inserting each item. A 100-track export therefore costs 5,050 units before any lookups. The default daily allocation for these operations is 10,000 units per project, so a shared project's quota would be a real limitation.

Unsupported YouTube Music web requests are another possible route, but that would be new reverse-engineered functionality, not a capability already provided by the current libraries. Ordinary YouTube playlists and YouTube Music library visibility also need an account test before promising identical behavior.

## Queue Upload feature

The implemented destinations are TIDAL, Deezer, Qobuz and Spotify. The dialog can create a playlist or append to an owned playlist. It is not a full playlist manager or automatic synchronization.

1. Take a fixed snapshot of the intended queue items in queue order. Exclude album rows and show their excluded count; the user chose individual tracks only.
2. Let the user select exactly one source. Include only queue tracks from that source and send their existing IDs to the same service. Do not match or convert anything; queue conversion is a separate, explicit user action.
3. Show the eligible track count across status tabs rather than relying on the visible tab. Creating a remote playlist doesn't upload local audio.
4. Ask for name, description and destination-supported visibility. Use a non-public default where supported and clearly distinguish private from unlisted.
5. Choose Create a new playlist or a listed owned playlist. New targets use the name, description and visibility inputs. Existing targets hide those inputs and append only. Skip repeated catalog IDs and IDs already present, preserve queue order for new additions, and retain all existing entries and metadata.
6. Keep the title Upload playlist throughout. Put the InfoBar directly below it, with explanations or outcome counts. Show the form's track, album-exclusion and duplicate-exclusion counts on separate lines. Existing-target duplicate previews use an owned-content read and never mutate playlists.
7. Show a Playlist created or Playlist updated label inside the result, followed by the playlist name in a selectable Settings-style display box. Keep the primary Open playlist footer action. Do not show a raw URL or a separate link row. Keep the known link and a partial-result report if an append fails. Don't automatically delete the playlist on failure.

Spotify uses explicit Settings OAuth. YouTube remains unsupported. Deezer and Qobuz reuse the existing Settings sign-ins. Source changes reset visibility to the selected service's non-public default. The initial source follows the first eligible queue track, or an existing album's source if there are no eligible tracks.

`IPlaylistWriter` in `FluentDL.Sources/Contracts/PlaylistCreation.cs` lets the dialog discover destinations through `SourceRegistry`, as the app already does for search and conversion. Each writer exposes its visibility choices, name limit, sign-in guidance and owned playlists. Listing failures leave new-playlist creation available and expose a read-only Refresh action. Duplicate playlist names get a short ID suffix in the picker to distinguish them. The dialog checks whether writing is available for the current account, not just whether the source implements the interface.

Field labels use the smaller secondary-text style used in Settings. Preview changes labels the three statistics lines. The InfoBar omits the all-status-tabs note and the sentence about matching, conversion and queue changes. Upload still includes eligible tracks across queue status tabs. Create a new playlist has no trailing dots. Existing selections expose an Open icon immediately left of Refresh, at the same size. This navigation action never submits a write or closes the dialog and is hidden for new targets.

Before appending, the writer rereads ownership and complete contents. It checks them again before each batch and verifies the resulting ordered contents after each write. A concurrent change stops further requests. These checks are not an atomic lock against another client. No mutation is automatically replayed after an uncertain response.

The queue toolbar shows every action with an icon and label when the whole action group fits its available width. When it does not fit, all actions switch together to icon-only controls with tooltips and accessible names. The group can sit below the count if necessary. Queue card titles and artists use character ellipsis and full-text tooltips instead of marquees. The preview pane is unchanged.

For a later manager, introduce playlist and playlist-entry models. Preserve playlist identity, catalog track identity, entry identity, positions and any version/snapshot token separately. Don't silently deduplicate songs, sort them by completion time, or retry append calls without checking whether the server already applied them.

## Next validation

Remaining validation with disposable test playlists:

- Deezer: private creation, 51-track ordering, owned listing, existing append, duplicate skipping, metadata preservation and cleanup passed. Renaming and removing entries are not implemented or live-tested.
- Qobuz: private creation, 51-track ordering, owned listing, existing append, duplicate skipping, metadata preservation and cleanup passed. Renaming, reordering and removing entries remain outside this feature. Public creation has offline coverage only.
- Spotify: obtain write consent through the app, restart and confirm persisted sign-in, then resume the user's existing playlist after rebuilding. Official endpoint, pagination, batching and encrypted-session lifecycle checks use simulated responses. No new live mutation authorization is implied.
- TIDAL: the production writer passed a live 51-track test across two batches, with complete order and duplicates verified and its test playlist removed. Fake HTTP tests cover partial failures, uncertain writes, cancellation and no automatic mutation retry. Separate developer-app OAuth is not required for the download client tested above, but remains untested as an alternative login.

## Current validation

Offline characterization tests cover creation, existing appends, ownership checks, pagination, duplicate filtering, partial and uncertain outcomes, cancellation, source changes and asynchronous duplicate-preview failures. The form cannot submit an existing target while its duplicate preview is pending or unavailable. Core tests remain a separate suite.

The opt-in live cases create disposable playlists, verify exact order, read them through the owned listing and duplicate-preview method, append with duplicate IDs, check unchanged metadata, and delete only their own created IDs. TIDAL and Qobuz cleanup verifies HTTP 404. Deezer cleanup verifies acknowledgement and a subsequent account-gateway permission error. No test appends to a pre-existing user playlist.

The Sandbox preview suite checks source switching, new and existing targets, duplicate-only uploads, a constant Upload playlist title, result labels and display boxes, topmost InfoBar placement, matching refresh/picker bounds, responsive toolbar labels, ellipsis and full-title tooltips. Separate state and layout suites check sign-out, album exclusion, keyboard focus and contrast themes. Screenshots require visual review in addition to UI Automation assertions. Synthetic handlers compile only into isolated preview output and never forward playlist requests to the network. Normal builds have an explicit test that excludes that support type.

The refreshed dialog passed all 14 preview checks. Native XAML measurements gave both the playlist input and refresh button a height of 32 logical pixels and the same top position. UI Automation exposed a 40-pixel rectangle for the ComboBox and a 32-pixel rectangle for the button. The test therefore records native layout geometry separately rather than assuming those automation rectangles describe identical parts of each control.

Final checks passed 237 offline characterization tests, 214 Core tests, five opt-in live playlist cases and 24 current UI checks. The UI total includes the 14 preview cases, six signed-out production cases and four narrow-window, keyboard and contrast cases. Screenshot review covered the updated forms and results, including light and contrast themes. The layout script's optional-dialog check now inspects the tree instead of treating a missing search match as a setup error. Normal x64 and ARM64 development builds succeeded, and the Sandbox has the normal build again. Changes remain local and uncommitted.

The subsequent smaller-label and inline-Open refinement passed 238 offline tests and all 14 preview UI cases. Those cases check that the new button is hidden for Create a new playlist, sits immediately left of Refresh at the same size for existing targets, and opens the exact selected URI without submitting an upload. Screenshot review covered the new captions, Preview changes heading, forms and results. The normal x64 build also succeeded. The follow-up normal-build Sandbox deployment returned `UnauthorizedAccess_IODenied_NoPathName`, including after closing only this task's preview app. Its extra signed-out and contrast reruns could not start. Other apps in the shared Sandbox were left running, and no permissions or shared deployment files were changed to bypass the error. The normal build is available locally, but replacing the registered Sandbox preview remains blocked.

At narrow widths the existing preview pane still limits title space. The user chose character ellipsis with full-title hover text rather than changing the preview layout. That truncation is intentional. Queue-card marquees were removed; the compact-list implementation is unchanged.

Builds still report existing package compatibility, nullable-reference and WinUI analyzer warnings. NuGet also reports existing ImageSharp and RestSharp security advisories. Those dependencies were not changed as part of this feature. No Native AOT or Store packaging claim is made by these development-build tests.

## Sources

- [DeezNET methods, exact installed commit](https://github.com/TrevTV/DeezNET/blob/711cdb99894d7f130c7420875576f3b4776f48ac/DeezNET/GWApi.cs)
- [QobuzApiSharp playlist service](https://github.com/DJDoubleD/QobuzApiSharp/blob/c227398a6281a55365bb7425992de5d245b3de94/QobuzApiSharp/Api/Service/Endpoints/QobuzApiService.Playlist.cs)
- [Music Assistant Qobuz provider](https://github.com/music-assistant/server/blob/dev/music_assistant/providers/qobuz/__init__.py)
- [Independent Qobuz client, playlist operations](https://github.com/fdenivac/python-qobuz/blob/5516a360256722fd8e66c22dfa1247055b65eb8b/src/qobuz/playlist.py)
- [Independent Qobuz client, user playlist creation](https://github.com/fdenivac/python-qobuz/blob/5516a360256722fd8e66c22dfa1247055b65eb8b/src/qobuz/user.py)
- [Qobuz API v0.2 documentation, hosted copy](http://openwin.shanling.com:9000/pdf/Qobuz_API_Documentation_v1.17.pdf)
- [SpotifyAPI.Web playlist client, exact installed commit](https://github.com/JohnnyCrazy/SpotifyAPI-NET/blob/8648117e852e705b4876e288d9b5414f5c7aa451/SpotifyAPI.Web/Clients/PlaylistsClient.cs)
- [Spotify create playlist](https://developer.spotify.com/documentation/web-api/reference/create-playlist)
- [Spotify add playlist items](https://developer.spotify.com/documentation/web-api/reference/add-items-to-playlist)
- [Spotify February 2026 changes](https://developer.spotify.com/documentation/web-api/references/changes/february-2026)
- [Spotify quota modes and user limits](https://developer.spotify.com/documentation/web-api/concepts/quota-modes)
- [TIDAL official API reference](https://tidal-music.github.io/tidal-api-reference/)
- [TIDAL OpenAPI document, including scopes and request limits](https://tidal-music.github.io/tidal-api-reference/tidal-api-oas.json)
- [python-tidal account playlist listing and creation](https://github.com/tamland/python-tidal/blob/master/tidalapi/user.py)
- [python-tidal playlist editing](https://github.com/tamland/python-tidal/blob/master/tidalapi/playlist.py)
- [YouTubeMusicAPI client, exact installed commit](https://github.com/IcySnex/YouTubeMusicAPI/blob/070ee18a9bb0686d0c1826eddfa2d1ae8884fb81/YouTubeMusicAPI/Client/YouTubeMusicClient.cs)
- [YouTube create playlist](https://developers.google.com/youtube/v3/docs/playlists/insert)
- [YouTube add playlist item](https://developers.google.com/youtube/v3/docs/playlistItems/insert)
- [YouTube quota allocation](https://developers.google.com/youtube/v3/determine_quota_cost)
