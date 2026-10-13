# TIDAL direct downloads

The download client is a port of the single-file PKCE experiment. Enable it in Settings under **TIDAL direct downloads**, then sign in for downloads. The ordinary TIDAL catalog login stays separate.

The embedded browser catches the Android redirect. If it is blocked, choose **Open in my browser instead**, sign in, and paste the full Page not found address into the dialog. The address contains a short-lived authorization code. The app stores the resulting session with Windows DPAPI.

Full tracks require a subscription or trial. If the opt-in is off, the download account is signed out, or TIDAL returns a preview, FluentDL uses its existing equivalent-download route. It does not save a preview as a full song. Download quality comes from the actual returned codec, so an AAC response gets an `.m4a` extension. FFmpeg copies audio into the final container, then FluentDL writes TIDAL's tags.

## Client configuration

Create a Gist containing a JSON file with this shape:

```json
{"client_id":"THE_DOWNLOAD_CLIENT_ID"}
```

Use its revision-independent raw URL, such as `https://gist.githubusercontent.com/OWNER/GIST_ID/raw/tidal-download.json`. A revision-specific URL remains pinned to that version.

Set repository variable `TIDAL_DOWNLOAD_CONFIG_URL` to that URL. Set repository secret `TIDAL_DOWNLOAD_CLIENT_ID` to the fallback download client ID. There is no download client secret. These are separate from the official API's `TIDAL_CLIENT_ID` and `TIDAL_CLIENT_SECRET`.

The release workflow supplies `FLUENTDL_TIDAL_DOWNLOAD_CONFIG_URL` and `FLUENTDL_TIDAL_DOWNLOAD_CLIENT_ID` at build time. Local builds can put those properties in `%USERPROFILE%\.fluentdl-secrets\build.props`. Settings also has an optional Gist URL override for testing.

Before a download sign-in, FluentDL checks the Gist with a five-second timeout. It reuses a successful result for an hour. Failure uses the saved client ID, then the bundled one. Only `client_id` comes from the Gist. The authentication and redirect addresses stay fixed in code.

A saved refresh token belongs to the client ID used to create it. Updating the Gist changes new sign-ins, but cannot move existing sessions to a new client. If TIDAL rejects an old session, the user must sign in again.

## Verification

`TidalDownloadTests` covers manifest ordering, returned codecs, encryption rejection, PKCE, refresh, Gist caching, preview fallback, and partial-file cleanup without network access.

`TidalDirectLiveTests` is an explicit manual test. Set `FLUENTDL_TIDAL_DOWNLOAD_TEST_SESSION` to a file containing the DPAPI-protected download session before running it. It downloads a full track through DownloadService and checks the duration, bit depth and tags. The file must belong to the same Windows user. The normal test run does not need this session.

To check the real Gist reader without opening a browser or contacting TIDAL, put the Gist URL in the local build properties and set `FLUENTDL_TIDAL_DOWNLOAD_GIST_TEST_URL` to the same URL. Run `TidalDownloadTests.RealGistSuppliesTheClientIdBeforeTheBundledFallback`. The test supplies a different bundled fallback and requires the remote ID to reach the cache and PKCE address. App logs say `TIDAL download client configuration source: Gist`, `Cache`, or `Bundled`, without printing the ID.

For the native embedded-browser startup check, install the dev package with `tools/ui-tests/Install-DevPackage.ps1 -BlankTidalLogin`. Set `FLUENTDL_UI_TESTS=1` and `FLUENTDL_TIDAL_BLANK_BROWSER_TEST=1`, then run `SettingsUiTests.TidalEmbeddedBrowserStartsOffline`. This diagnostic build opens only `about:blank` in the login dialog. Reinstall a normal dev build afterwards before testing a real sign-in.
