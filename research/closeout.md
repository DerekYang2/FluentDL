# Playlist and authentication closeout

Updated 18 October 2026. This records validation of the playlist work and its preparation on `feature/queue-playlists`, based on merged master at `38b5f73`. The original `research/playlists` worktree remains preserved separately. No host app restart, real Spotify authorization or live playlist mutation formed part of the automated closeout. Derek separately confirmed a real 900-track Spotify upload succeeded.

## Changes

- Upload playlist now measures a finite form width, capped at 440 logical pixels, and adjusts it when the app becomes narrower. Source, Name, Description and Visibility align. The dialog uses the built-in ContentDialog vertical scrolling.
- The all-status-tabs sentence is gone. Queue selection and upload behavior are unchanged.
- Spotify access tokens remain in memory. CurrentUser DPAPI protects the saved refresh token, scopes and account/client binding. The record contains no access token or client secret.
- Startup refreshes the saved token under the same developer credentials, then verifies the same account before publishing signed-in state. Restoration never opens a browser. Earlier memory-only sign-ins need one new explicit sign-in.
- Sign-out and credential changes clear the saved session. Saving unchanged credentials preserves the active session and avoids a competing refresh request. Refresh rotation is saved before the token reaches an API request. A failed save can retry without repeating the OAuth refresh or an API mutation.
- Corrupt storage, rejected tokens, missing required playlist permissions and account mismatches invalidate the saved session. Transient failures retain it. Generation checks prevent a late response from restoring a signed-out account.

## Integration

The temporary copy `C:\Users\derek\AppData\Local\Temp\opencode\fdl-closeout-c37bc2c5` combines these versions without committing or changing any source branch:

| Change | Revision |
| --- | --- |
| Master baseline | `1e928516e948298441aeb3b800b0167bc1428cf3` |
| PR #40, UI workflows | `9415dbe1bd447afd9768acdc7e651d1769715800` |
| PR #41, Spek integration | `d7925fb374862d5d4c08e5cd544f7a58e8394950` |
| PR #42, branding | `b2137e8ca8e16ed34075bb26da33700048b5064c` |
| Playlist work | Local tracked diff and nonignored new files |

All patches applied without merge conflicts. After PRs #40, #41 and #42 merged, a clean preparation worktree applied the same playlist changes on the actual merged master. Its source/project files match the validated combined copy. The two PR #40 assertions now use `QueuePage.FormatQueueCount` directly. Spek's spectrogram calls, automation IDs, AAC options and the new branding remain intact.

## Validation

- The integrated offline characterization suite passed 428 cases. It excludes Live, UI, UIProbe and Deferred categories. Tests use synthetic credentials and responses for Spotify session behavior.
- The integrated Core suite passed 230 cases.
- Ten Sandbox notice/layout/navigation checks passed. All seven screenshots passed visual review. Native measurements show the four form fields at 440 logical pixels with matching left edges. A 560-pixel app window has no clipped description or dropdown.
- Four Sandbox layout checks passed for the 800x740 window, keyboard Tab, toolbar command reachability and a Contrast theme. Reviewed all three screenshots. The existing narrow Queue preview pane still limits card text space; changing that pane is not part of this closeout.
- Normal integrated x64 and ARM64 development builds succeeded with zero errors. Existing compatibility, nullable, analyzer and dependency-advisory warnings remain. Development builds do not validate Native AOT, Store submission or production TIDAL release credentials.
- All 14 integrated Spek Sandbox checks passed. Reviewed the nine screenshots and exported PNG. Coverage includes Local Explorer, both Queue layouts, palette/FFT/channel controls, zoom/reset, profile/selection clearing, focused Escape, dB spinners, PNG export and decoder cleanup. The earlier interrupted run is not counted as passing; the completed rerun uses a fresh artifact directory.
- The combined UI total is 28 passing checks. This does not claim a rerun of PR #40's credential-dependent live workflow suite. The comparison preserved its source and compiled its tests, and retained the two queue-count assertion updates.
- Reviewed the new token storage/lifecycle and dialog sizing code. The session tests cover real Windows DPAPI with synthetic token responses, sign-out during restoration, rotation persistence, storage-save retry, and unchanged-credential saves that preserve the restored private-read client. No interactive Spotify authorization occurred.

Evidence:

- Integrated build/test logs and TRX files: `C:\Users\derek\AppData\Local\Temp\opencode\fdl-closeout-c37bc2c5\closeout-evidence`.
- Dialogs and inline links: `C:\Users\derek\AppData\Local\Temp\opencode\dialog-padding-15f6e4245a884a978771478be6b478bb`.
- Keyboard and Contrast: `C:\Users\derek\AppData\Local\Temp\opencode\playlist-layout-942451e5e5fc4012b89328d3e497e717`.
- Spek UI and PNG export: `C:\Users\derek\AppData\Local\Temp\opencode\fdl-closeout-c37bc2c5\closeout-evidence\spek-ui-final`.
- The final build logs are `x64-closeout.log` and `arm64-closeout.log`. The final characterization result is `test-results/combined-characterization-closeout.trx`; the Core result is `test-results/combined-core.trx`.

## Remaining checks and decisions

- The user must sign in to Spotify once through the updated app, restart, then confirm restoration. No password, authorization code or verification code belongs in chat or test logs.
- Resuming the existing Spotify playlist that stopped around 600 tracks is an optional follow-up for duplicate skipping. Automated tests cover it; Derek confirmed a fresh 900-track upload succeeded. Do not claim the resume check as manually verified. No new live write authorization is implied.
- Validate production TIDAL credentials through a release build and confirm the distribution policy for unofficial downloads.
- Audible previews and paid-account Deezer/Qobuz lossless output still need manual checks.
- Derek authorized preparing a few commits, opening the playlist PR and merging it after checks. Queue filters and in-app updating are not part of this change. No release or version bump is authorized by the merge.

The standard preview-result script retains the same native field-alignment assertions used by the successful notice batch. Earlier local PR drafts and the temporary compatibility patch are not needed in the prepared branch.

The Obsidian note `FluentDL closeout checklist` separates these checks from optional backlog items, including TIDAL artist links, test-project separation, ZIP picker automation, FFmpeg/yt-dlp notice links, Spek A/B comparison and standalone Store distribution.
