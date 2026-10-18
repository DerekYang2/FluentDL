# Spotify queue playlists

Spotify is an Upload playlist destination. It uses the official Web API, not web-player GraphQL. Upload takes only individual Spotify tracks from the queue snapshot, in queue order across all status tabs. It does not convert tracks or change the queue.

## Authorization

Open Settings > Authentication > Spotify. Save a Spotify developer client ID and secret, then press Sign in. The former Private Playlist Prompt card now contains this button, the explanatory text and the copyable callback URI. Register `http://127.0.0.1:5543/callback` in that developer app. Anonymous web-player authentication and client-credentials authentication cannot write user playlists.

The Client ID, Client secret and Save cards appear above Sign in (optional). Sign in stays disabled until both credentials are nonempty, saved and match the fields. Editing either field disables it until the changes are saved or reverted. Sign out and Cancel sign-in remain available without matching credentials. Optional means user sign-in is optional for catalog use, not that the credentials are optional during authorization.

For an app in Development Mode, open Spotify Developer Dashboard, select your app, then open Settings > Users Management. Choose Add new user and add the name and Spotify email address of the account you will sign in with. The developer app owner must have Spotify Premium. Spotify warns that an account can complete browser sign-in without being allowlisted, but its API requests will then return HTTP 403. This allowlist requirement does not apply to an app with extended quota access. See [Spotify's quota-mode and allowlist instructions](https://developer.spotify.com/documentation/web-api/concepts/quota-modes).

Sign-in requests `playlist-read-private`, `playlist-modify-private`, `playlist-modify-public` and the existing collaborative-read scope. The app verifies the authenticated Spotify profile before publishing the user session. A browser consent callback alone does not count as successful sign-in. The same session supports private imports and playlist uploads.

OAuth callbacks must match a random state and the current credential generation. Denial, leaving Settings, cancelling sign-in and a two-minute timeout stop sign-in and close the local callback listener. Access tokens stay in memory, with a shared refresh lock for SDK reads and playlist HTTP calls. The refresh token, granted scopes and account/client binding are saved with Windows CurrentUser DPAPI encryption. The encrypted record contains no client secret or access token. Startup refreshes it under the same developer app and verifies the same user profile before restoring signed-in state. Neither startup, uploading nor pasting a playlist link opens browser authorization.

Sign out clears the saved record. Changing either saved credential invalidates it; saving the same credentials preserves the active user session without another refresh request. Rotated refresh tokens are saved before use. Invalid/revoked tokens, missing permissions, corrupt storage or a different account require explicit sign-in again. Offline, timeout and server failures do not delete the saved refresh token. A sign-out or credential change prevents late refresh responses from saving or publishing the old session. The old auto-prompt preference is no longer read or shown; existing saved values remain untouched. Earlier memory-only sign-ins require one new explicit sign-in after installing this change.

When Spotify needs user access, its InfoBar gives a clickable inline Settings > Authentication > Spotify path, not a separate button. The shortcut opens Settings, expands Spotify and scrolls it into view. The upload dialog closes before navigating. A public playlist can still import anonymously. An inaccessible anonymous playlist gets a conditional sign-in hint rather than a claim that every failed link is private. Sign in through Settings, then retry the original action. No authorization attempt creates or modifies a playlist.

## Visibility and ownership

New playlists default to Not shown on profile. The other choice is Shown on profile. Spotify's API `public` flag controls profile visibility, not access privacy. The dialog says to change access/privacy in Spotify itself. These choices must not say Private, only you.

Existing destinations include only playlists owned by the authorized Spotify user, not followed playlists or playlists owned by another collaborator. Existing uploads append without changing the name, description, profile visibility, collaboration setting or existing entries. Local files, episodes and unavailable entries remain in their original positions during verification. Relinked tracks use their original `linked_from.id` when Spotify supplies one.

## Transport and verification

- Create uses `POST /v1/me/playlists`.
- Owned listing uses `GET /v1/me/playlists`, with pages of up to 50.
- Reads and appends use `/v1/playlists/{id}/items`. The deprecated `/tracks` routes and older SDK playlist mutation methods are not used.
- Appends contain at most 100 `spotify:track:` URIs. The app validates 22-character ASCII base62 IDs and preserves case and leading zeros. Different IDs remain distinct.
- Duplicate checks remove repeated queue IDs and IDs already in the destination. Each batch rereads ownership and ordered contents before writing, then verifies the full playlist after the acknowledgement. Reads check the playlist snapshot before and after pagination.
- Playlist requests share a budget of two in flight with 200 ms spacing. This is an application setting, not a documented Spotify quota. Read-only requests have bounded retries and honor `Retry-After`. Writes never retry after HTTP failures, cancellation or lost acknowledgements. Uncertain outcomes keep any known playlist link and tell the user to check Spotify before retrying.

There is no automatic playlist cleanup or deletion. A failure keeps any playlist already created and confirmed additions. Appending is not an atomic lock against edits from another Spotify client.

Snapshot movement alone no longer stops an upload. Verification permits up to three read-only passes. It accepts a stable snapshot or two identical complete ordered reads, with ownership checks on every pass. After an append, it also waits for the expected contents before confirming that batch. Changing page totals discard that pass. Continually changing contents still stop the upload; verified item/order mismatches still stop further batches. These read checks never replay a create or append request.

Regression cases cover a 901-track upload whose snapshots start moving after track 600, delayed read-back after acknowledged writes, and resuming an existing 600-track playlist with duplicate skipping. They do not claim a live Spotify upload test. A retry must target the existing playlist, not create another one.

## Validation

`SpotifyPlaylistTests` uses fake official HTTP responses. It covers batching, pagination, owned-only targets, duplicate skipping, profile visibility, base62 IDs, mixed existing item types, relinking, changed snapshots and accounts, cancellation, refresh serialization and no-replay write failures. Live validation requires the user to complete Spotify OAuth themselves. Do not print tokens, passwords or authorization codes, or use an existing playlist for write experiments without authorization.

Before the automatic upload-prompt change, the x64 characterization build compiled the app and Settings XAML, and all 173 targeted Spotify, shared-dialog, TIDAL, Deezer/Qobuz, Qobuz availability and request-policy cases passed. No live Spotify authorization or playlist mutation was part of that run. Native keyboard, mouse and High Contrast checks have not been claimed for this flow.

Official references checked on October 17, 2026:

- [Create playlist](https://developer.spotify.com/documentation/web-api/reference/create-playlist)
- [Add playlist items](https://developer.spotify.com/documentation/web-api/reference/add-items-to-playlist)
- [Get playlist items](https://developer.spotify.com/documentation/web-api/reference/get-playlists-items)
- [Get current user's playlists](https://developer.spotify.com/documentation/web-api/reference/get-a-list-of-current-users-playlists)
- [Playlist concepts](https://developer.spotify.com/documentation/web-api/concepts/playlists)
