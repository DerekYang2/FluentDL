# Deezer and Qobuz queue playlists

Qobuz can acknowledge `playlist/addTracks` while omitting a requested track. The writer counts all verified additions in that batch, rather than only the matching prefix before an omitted track. If the original contents remain unchanged and the additions are an ordered subset of the requested batch, it continues with the next batch using the verified contents as its baseline. It never resends the omitted tracks within that upload. After processing every batch, any omissions produce an incomplete result with their count separate from duplicates. Unexpected additions, reordering or changes to existing tracks still stop the upload.

On October 17, 2026, an authorized test on playlist `71891184` reproduced a 50-track request that added 49 tracks and omitted `128727296`, `Know Me`. A direct `track/get` request for that ID returned HTTP 404 with the same account. The old prefix-based confirmation reported zero additions because that ID came first in the retry batch. Test additions were removed by their new playlist-entry IDs, and the original 98 track IDs and their order were verified against the saved baseline. Evidence is in `C:\Users\derek\AppData\Local\Temp\opencode\qobuz-71891184-investigation` on the development machine.

After the fix, 88 targeted offline writer and dialog cases passed. The regressions include 812-track new and existing uploads across 17 batches with omissions, duplicate handling on retry, an entirely omitted batch, and stops for unexpected additions or changed order. This validation did not upload the full 812-track queue to the live playlist.

Queue Upload has Deezer and Qobuz destinations. Each uses its existing Settings sign-in. Select one service, then choose Create a new playlist or an existing playlist owned by that account. New playlists ask for a name, optional description and Private or Public visibility. Private is the default. Existing selections hide those fields and append only, without changing metadata or removing existing tracks. Upload includes only that service's individual tracks across all queue status tabs, in queue order. It reports excluded album rows and leaves the queue unchanged.

The dialog title is always Upload playlist. Its InfoBar is the first content item, above the form or result, and contains explanations, progress or outcome messages. The form uses smaller secondary-text field labels. A Preview changes heading groups tracks to save, excluded albums and excluded duplicates on separate lines. Selecting an existing playlist reads its owned contents without mutation so the duplicate count includes tracks already present. Upload stays disabled until that check finishes. The writer rereads contents at submission, so final counts can differ if another client changes the playlist.

The picker starts with Create a new playlist, without trailing dots. Existing selections show an Open icon immediately left of Refresh. These buttons use the same size. Inline Open launches the selected playlist without submitting an upload or closing the dialog. It is hidden for new playlists. The result screen retains its separate primary Open playlist footer action.

The result has a Playlist created or Playlist updated label and the playlist name in a selectable, themed display box matching the Settings status-box pattern. It keeps the primary Open playlist footer button and Close. Partial or uncertain outcomes keep Open playlist whenever a link is known. The writers never delete a partial playlist or retry an uncertain mutation. The playlist picker and refresh button share one header-free grid row and stretch to the same height.

## Service behavior

All writers skip duplicate catalog track IDs. This removes repeated occurrences from the upload snapshot and skips IDs already present in an existing target, while retaining first-occurrence queue order. Existing duplicates in the destination remain untouched. The result reports Added and Skipped separately. An all-duplicate existing upload completes with zero mutations and still offers Open playlist. Different releases with different service IDs are not duplicates, even if their names or ISRCs match.

Deezer uses its web gateway with the active ARL, SID and form token. It does not use DeezNET's retrying write methods. Appends send at most 50 IDs with `offset=-1`. Every batch reads back the full ordered playlist through the authenticated gateway. The gateway silently truncated a live test name at 50 characters. The dialog and writer now enforce that limit before creation.

Qobuz uses the current app ID and user token in headers. It sends form-encoded POSTs to the v0.2 playlist API. Each batch has at most 50 unique IDs, sends `no_duplicate=1`, and ends with an ordered `track_ids` read-back. Playlist links use `https://play.qobuz.com/playlist/{id}` for the picker and all upload results, including incomplete uploads. The `open.qobuz.com` playlist links redirected to the home page in the reported case. Existing imports still accept either hostname. TIDAL uses the same shared duplicate filter and sends `onDuplicates=SKIP`.

Both writers verify new-playlist visibility before adding tracks. Existing-playlist uploads read ownership and contents before appending. They check ownership and compare contents again before each batch, then stop if the playlist has changed. This is not an atomic lock against another client. Provider-side duplicate handling and post-write checks cover changes between requests without replaying uncertain mutations. They also stop if the account changes or signs out. Readiness requires the adapter's current authenticated state, not saved credentials or stale HTTP headers alone. Public creation has offline coverage. Live tests create private Deezer/Qobuz playlists and unlisted TIDAL playlists only.

Qobuz listings use `playlist/getUserPlaylists` with offset pagination and filter by owner ID. Deezer listings use the signed-in profile's playlist tab and filter `PARENT_USER_ID`. The tested gateway returned the complete tab and ignored small `nb`/`start` values. The writer checks the reported total and rejects incomplete or repeated pages rather than presenting a truncated list as complete. TIDAL lists playlists filtered by `owners.id=me`, verifies owner linkage and follows its cursor links. Listing failure does not prevent creating a new playlist. Refresh retries the read only.

## Offline tests

```powershell
dotnet test FluentDL.Characterization.Tests/FluentDL.Characterization.Tests.csproj -p:Platform=x64 --filter 'TestCategory!=Live&TestCategory!=UI&TestCategory!=UIProbe'
```

`DeezerQobuzPlaylistTests` uses fake HTTP handlers. It covers batch boundaries, source credentials, visibility, duplicate behavior, read-back pagination, account changes, cancelled requests, malformed responses, permission errors, rate limits and uncertain writes. `PlaylistDialogTests` covers source selection, visibility reset, stale sign-in checks and same-service queue snapshots.

## Explicit live writes

Use the external characterization-test secrets file. The credential keys are `deezer_arl`, `qobuz_id`, `qobuz_token`, `qobuz_app_id` and `qobuz_app_secret`. Do not put their values in source code, arguments or test output.

```powershell
$env:FLUENTDL_PLAYLIST_WRITE_TESTS = '1'
dotnet test FluentDL.Characterization.Tests/FluentDL.Characterization.Tests.csproj -p:Platform=x64 --filter 'FullyQualifiedName~DeezerQobuzPlaylistLiveTests' --logger 'console;verbosity=detailed'
Remove-Item Env:FLUENTDL_PLAYLIST_WRITE_TESTS
```

The tests create uniquely named `FluentDL test` or `FluentDL playlist writer test` playlists. They verify visibility and exact ordered IDs, find those same disposable IDs through the owned-playlist listing, and append a request containing both existing and queued duplicates. They check the Added/Skipped counts, retained original contents and unchanged metadata. They never append to a pre-existing user playlist. Cleanup runs in `finally` and deletes only IDs returned by the test's own creation requests. Deezer confirms deletion and denies subsequent gateway reads with `PERMISSION_ERROR`. Qobuz confirms deletion and returns HTTP 404. Recovery names appear in test output if a request is interrupted before cleanup can identify the created playlist.

On 17 October 2026, all three production writers passed owned listing, appending to their disposable playlists, duplicate skipping and metadata-preservation checks. Both Deezer and Qobuz also passed 51-track creation tests across two batches. Deezer passed an all-duplicate append with zero additions. All diagnostic and production test playlists were deleted. No pre-existing user playlist was edited.

## Synthetic UI tests

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/ui-tests/Test-PlaylistResults.ps1
```

This script uses the existing Windows Sandbox target. It seeds a mixed-service queue, builds the opt-in preview into separate output directories, and never calls remote playlist APIs. Its handlers accept synthetic credentials only. The tests check source-specific counts and visibility defaults, complete and partial results, existing-playlist selection and hidden creation fields, duplicate-only uploads, exact Open playlist URIs and unchanged queue counts. Deezer and Qobuz also record synthetic uploaded IDs so the script can check service filtering and order. The toolbar check resizes the window and checks that all actions switch together between icons with labels and icon-only buttons.

`Test-PlaylistDialog.ps1` checks signed-out, ready and album-only states with the same isolated preview handlers. This keeps the synthetic account from contacting remote playlist-listing APIs. Its `-ProductionSignedOutOnly` mode instead uses an already-built normal app and supplies no signed-in sessions. `Test-PlaylistDialogLayout.ps1` checks the narrow toolbar/dialog, keyboard focus and contrast theme. Always keep guest PIDs paired with `--on sandbox`. Restore a normal build after preview testing with `winapp run FluentDL/FluentDL.csproj --on sandbox --no-launch --json --arch x64`.
