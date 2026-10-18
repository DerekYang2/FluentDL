# TIDAL direct downloads

The download client uses the single-file PKCE experiment's flow. Open Settings > Authentication > TIDAL and choose Sign in in the Account group. This account supports full downloads and playlist uploads. There is no automatic login prompt. When an action needs an account, its InfoBar contains a clickable Settings path. The optional Search API group below takes your own developer app's client ID and secret; catalog search and public links can work without an account sign-in.

Search API client credentials do not use a redirect URI. Account sign-in is a separate PKCE flow with the fixed redirect `https://tidal.com/android/login/auth`, which the embedded dialog or browser fallback handles. Normal users do not need to register a redirect for this built-in account client. An optional account client-ID override must support that redirect and the existing PKCE flow; the Search API ID/secret fields do not configure it.

The embedded browser catches the Android redirect. If it is blocked, choose **Open in my browser instead**, sign in, and paste the full Page not found address into the dialog. The address contains a short-lived authorization code. The app stores the resulting session with Windows DPAPI.

The status button shows API and downloading identities. When signed in for downloads, official catalog requests prefer that same account token, including searches, metadata and previews. It displays the account email when TIDAL supplies one, or the username otherwise. Without a download session, catalog access uses the optional separate API account flow if enabled, then custom developer credentials, then bundled credentials. Anonymous catalog access has no account email. Download sign-in and sign-out messages identify the account. The account identity is part of the encrypted session, not the app log.

Catalog requests use the same `TidalDownloadClient` instance as downloads, so the next request observes sign-in, sign-out and token refresh. Country settings still override the account's country. A revoked refresh token clears the download session and lets catalog requests use app credentials. An official API 401 forces one download-token refresh; if the refreshed token is also rejected, catalog access falls back without deleting a download session that may still work for audio. Permissions errors, rate limits and temporary refresh failures do not trigger a credential switch. Rate limits respect `Retry-After` with bounded retries. Sharing account tokens does not establish that TIDAL rate limits are per user rather than per client or IP.

The download subscription check uses `premiumAccess`, not the account's `ACTIVE` status, which can also describe a free account. The UI reports Active, Not active or Unavailable. Active includes trial access and does not mean the user has been charged. Successful checks are cached for one minute.

Full tracks require a subscription or trial. When TIDAL can't provide the full track (signed out, an expired sign-in, playback denied, a preview only, or encrypted audio), the download fails with that reason, as Deezer and Qobuz downloads do. It never saves a preview as a full song. The older fallback to an equivalent on another source is kept behind `DownloadService.TidalEquivalentFallback`, a code-only switch that is off; the live `DownloadTests` TIDAL case turns it on to keep that route tested. Download quality comes from the actual returned codec, so an AAC response gets an `.m4a` extension. FFmpeg copies audio into the final container, then FluentDL writes TIDAL's tags.

## Client configuration

Create a Gist containing a JSON file with this shape:

```json
{"client_id":"THE_DOWNLOAD_CLIENT_ID"}
```

Use its revision-independent raw URL, such as `https://gist.githubusercontent.com/OWNER/GIST_ID/raw/tidal-download.json`. A revision-specific URL remains pinned to that version.

Set repository variable `TIDAL_DOWNLOAD_CONFIG_URL` to that URL. Set repository secret `TIDAL_DOWNLOAD_CLIENT_ID` to the fallback download client ID. There is no download client secret. These are separate from the official API's `TIDAL_CLIENT_ID` and `TIDAL_CLIENT_SECRET`.

The release workflow supplies `FLUENTDL_TIDAL_DOWNLOAD_CONFIG_URL` and `FLUENTDL_TIDAL_DOWNLOAD_CLIENT_ID` at build time. Local builds can put those properties in `%USERPROFILE%\.fluentdl-secrets\build.props`. Settings also has an optional download client ID that overrides the Gist. Saving it applies to the next sign-in.

Before a download sign-in, FluentDL checks the Gist with a five-second timeout. It reuses a successful result for an hour. Failure uses the saved client ID, then the bundled one. Only `client_id` comes from the Gist. The authentication and redirect addresses stay fixed in code.

A saved refresh token belongs to the client ID used to create it. Updating the Gist changes new sign-ins, but cannot move existing sessions to a new client. If TIDAL rejects an old session, the user must sign in again.

## Verification

`TidalDownloadTests` covers manifest ordering, returned codecs, encryption rejection, PKCE, refresh, Gist caching, the failure reasons with the fallback on and off, and partial-file cleanup without network access.

`TidalCatalogSessionTests` covers account preference, sign-in/out changes without restarting, refresh under the original client ID, concurrent refresh, revoked sessions, catalog 401s, 403/429 responses, transient refresh failure and cancellation. `TidalCatalogLiveTests` uses `FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION` for an opt-in read-only check through the real source code. It disables app credentials and verifies search pagination, advanced search, metadata, covers, preview manifests, album/playlist paging and ISRC/UPC lookup. Any rotated session is saved back to the supplied encrypted test file; app settings are not touched.

`TidalDirectLiveTests` is an explicit manual test. Set `FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION` to a file containing the DPAPI-protected download session before running it. It downloads a full track through DownloadService and checks the duration, bit depth and tags. The file must belong to the same Windows user. The normal test run does not need this session.

To check the real Gist reader without opening a browser or contacting TIDAL, put the Gist URL in the local build properties and set `FLUENTDL_TIDAL_DOWNLOAD_GIST_TEST_URL` to the same URL. Run `TidalDownloadTests.RealGistSuppliesTheClientIdBeforeTheBundledFallback`. The test supplies a different bundled fallback and requires the remote ID to reach the cache and PKCE address. App logs say `TIDAL download client configuration source: Gist`, `Cache`, or `Bundled`, without printing the ID.

For the native embedded-browser startup check, install the dev package with `tools/ui-tests/Install-DevPackage.ps1 -BlankTidalLogin`. Set `FLUENTDL_UI_TESTS=1` and `FLUENTDL_TIDAL_BLANK_BROWSER_TEST=1`, then run `SettingsUiTests.TidalEmbeddedBrowserStartsOffline`. This diagnostic build opens only `about:blank` in the login dialog. Reinstall a normal dev build afterwards before testing a real sign-in.
