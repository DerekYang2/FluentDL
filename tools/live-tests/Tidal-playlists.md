# TIDAL playlist API probe

The standalone diagnostic compares the unofficial account API and the official API with the same saved download token. The app's queue action now uses `TidalPlaylistClient`, an official API writer that shares that session without another login flow.

## Queue UI

On the Queue page, **Upload** sits beside **Download** in the right-hand actions and opens **Upload playlist**. The action group wraps below the count when it cannot fit on one row. Select TIDAL, enter a name and optional description, and choose unlisted or public visibility. The dialog snapshots all individual TIDAL tracks across status tabs when it opens, preserves their queue order, and shows how many TIDAL album rows it excluded. It never expands albums, converts tracks, removes queue rows or overwrites an existing playlist. Deezer and Qobuz are also available with their existing sign-ins and private/public visibility. See [their live test instructions](Deezer-Qobuz-playlists.md).

Creation and appending need the download account sign-in. Catalog client ID/secret access alone cannot write a playlist. Choose Create a new playlist or an existing playlist owned by the account. Existing selections hide the name, description and visibility controls and never change those values. Unlisted is the new-playlist default, and anyone with the link can access an unlisted playlist. Appends run sequentially in batches of 50 unique track IDs. Duplicates are always skipped, including queue repeats and IDs already in an existing target. Existing duplicate occurrences are not removed. The server request also sends `onDuplicates=SKIP`.

The title remains Upload playlist in every state. The InfoBar is always the first content item and holds explanations or outcome counts. The form has separate lines for tracks to save, excluded albums and excluded duplicates. Existing selections trigger an owned-content read to calculate those counts before upload. There is no separate ownership note under the picker or explanatory caption below the form.

After completion, the form is replaced with a Playlist created or Playlist updated label and the playlist name in a selectable Settings-style display box. The primary footer button becomes **Open playlist**; there is no copy button or raw URL in the result view. Partial results keep the same action when a playlist ID is known. Cancellation and ambiguous network failures keep any created playlist. Opening a result never submits a second creation; close it and check TIDAL before trying again after an uncertain result.

`TidalPlaylistTests` and `PlaylistDialogTests` cover batching, ordering, duplicate skipping, ownership, visibility, source filtering, album exclusion, validation, cancellation and partial/uncertain failures without network access. `TidalPlaylistLiveTests` requires both the encrypted session path and `FLUENTDL_PLAYLIST_WRITE_TESTS=1`. It creates 51 distinct tracks across two requests and verifies their order through paginated official reads. It then finds that disposable playlist through the owned listing and appends a request containing existing and queued duplicates. The test checks counts and unchanged metadata, deletes only its own test playlist and checks for 404. This test passed on 17 October 2026.

`tools/ui-tests/Test-PlaylistDialog.ps1` drives the isolated preview in Windows Sandbox using synthetic queue rows, account sessions and playlist handlers, never real credentials. Its `-ProductionSignedOutOnly` mode checks an already-built normal app without signed-in sessions or playlist network calls. `Test-PlaylistDialogLayout.ps1` checks the narrow toolbar/dialog, keyboard navigation and a contrast theme. Both emit structured results and screenshots on the host. The fixture and window helpers refuse to run outside the Sandbox account.

`Test-PlaylistResults.ps1` uses an isolated preview build with synthetic HTTP responses to exercise source switching, successful and partial uploads for all three services, and the primary **Open playlist** action. Preview navigation records the requested URI locally instead of launching a browser, so the test verifies the exact target without opening its synthetic URL. `PlaylistDialogPreview.targets` includes the fake handlers only when `FluentDLPlaylistUiPreview=true`, and the test uses separate `obj/playlist-ui-preview` and `bin/playlist-ui-preview` directories. The fake handlers are absent from normal app builds, never forward requests to the network and refuse anything other than synthetic account credentials. Their sample playlist URLs are not real uploaded playlists. No production endpoint can be overridden through settings or environment variables.

## Standalone diagnostic

Use a test session file containing the DPAPI-protected JSON session from `TidalDownloadClient`, as with `TidalDirectLiveTests`. It must belong to the same Windows user. Do not pass access tokens or refresh tokens as arguments, paste them into a terminal, or check them into the repository.

From the repository root, list playlists without changing them:

```powershell
dotnet run --file tools/live-tests/TidalPlaylistProbe.cs -- C:\path\to\test-session.bin
```

The probe refreshes expired tokens with their original client ID and saves any rotated token encrypted back to the supplied test file. Do not run it concurrently with another process using that test session. It does not update FluentDL's settings. HTTP redirects are disabled, requests have a 30-second timeout, and output excludes response bodies, account identities and playlist names.

To test writes explicitly:

```powershell
dotnet run --file tools/live-tests/TidalPlaylistProbe.cs -- C:\path\to\test-session.bin --write
```

For each API, the write probe creates a uniquely named disposable playlist, adds three track IDs including a duplicate, verifies their order and removes the playlist in a `finally` block. The official test also verifies unlisted visibility. It only deletes IDs returned by its own create requests, never an existing playlist selected by the user. There are no automatic mutation retries.

The probe prints each created test playlist's ID so it can be removed manually if a network failure or process termination interrupts cleanup. A successful run requires deletion to return success and a subsequent read to return 404. A timeout during creation can leave a playlist whose ID was never received; check the account for names starting with `FluentDL playlist probe` after an interrupted or failed run.

## Results on 17 October 2026

The existing download session worked with both APIs. Its refreshed token reported `r_usr`, `w_usr` and `w_sub`.

- Both APIs listed the account's playlist collection.
- Both created playlists, appended three tracks and returned the same track order with the duplicate retained.
- Official creation respected `UNLISTED` visibility.
- Official reads and deletion worked independently of the unofficial API.
- Every test playlist was deleted and subsequent reads returned 404. No pre-existing playlist was changed.
- The unofficial append requires the playlist's ETag in `If-None-Match`. Without it, the first attempt returned 412.

The read-only probe also checks official track and album searches, track metadata with related artists/albums/cover art, album item references and track positions, and ISRC lookup. These all passed with the download token before catalog token selection changed. `TidalCatalogLiveTests` then verified the same integration through FluentDL's actual source methods, including pagination, preview manifests and UPC lookup, with all app-credential fallbacks disabled.

Prefer the official playlist endpoints with the existing download session if these results continue to hold. Keep the token and refresh logic tied to the download client ID. The two request formats in the probe can guide separate playlist implementations; they are not yet production clients or an automatic fallback system.
