# Authentication and Settings navigation

Spotify and TIDAL use explicit sign-in buttons under Settings > Authentication. Missing account access does not open a browser from Upload playlist or a pasted link. Spotify's old Private Playlist Prompt toggle is gone. The old saved preference remains untouched but no code reads it.

Spotify's Sign in card keeps the redirect URI, copy button and developer allowlist instructions. Sign-in requests private-read and playlist-write permissions, then checks the authenticated profile before marking the session ready. A Development Mode user who completes browser consent but receives HTTP 403 cannot become signed in through that attempt. Access tokens stay in memory. The refresh token, scopes and account/client binding now use Windows CurrentUser DPAPI encryption for restoration across restarts. Restoration never opens a browser and must verify the same account. Sign-out and credential changes invalidate the saved session. Transient failures retain the encrypted record for a later startup.

TIDAL's account is shared by full downloads and playlist uploads. Catalog search and public links can use existing catalog access without this account login. An inaccessible private playlist can need account access too. A missing public playlist or an HTTP failure does not prove that a playlist is private, so link messages use conditional wording. Subscription, country and content restrictions are separate from missing authentication.

## Navigation

`SettingsNavigation` maps known destinations to a typed `SettingsSection` and an inline clickable Settings path. The page expands and scrolls the requested section into view. Upload, lyrics and backup dialogs dismiss themselves before navigating. Search and Queue use persistent InfoBars for authentication failures and missing download-location notices. There are no separate Open settings buttons. Links such as `fluentdl://settings/spotify` work inside the app's parsed InfoBar text. Only known routes work; other hosts, arbitrary paths, numeric enum values, query parameters and fragments do not navigate. These are internal UI links, not a newly registered Windows protocol handler.

TIDAL download preflight runs before directory selection, queue result resets or filesystem writes. If a selected queue download batch includes TIDAL without an account, the batch does not start. Sign in and retry it. The existing code-only equivalent-download fallback remains independent.

## Service support and account details

Authentication has an always-visible Service Access card above the four provider expanders. Its columns are Service, Search, Playlist URLs, Downloads and Playlist upload. Cells show a green dot and Ready, an amber dot and Limited or Not checked, or a red dot and Unavailable. Hover text explains the reason. The dash means not applicable or no sign-in needed. Direct Spotify downloads and public Deezer search use the dash.

The overview uses a non-collapsible SettingsCard with the standard header typography, an information icon and a secondary description directly under the title. Service names have dots from the existing source-badge color converter. Neutral cells use a wide dash. The description says to hover over a cell and explains that the data reflects current account access, without repeating a color legend.

The Service Access card's header icon is ContactInfo, U+E779. The four provider headers use FIDOPasskey, U+EE7E, when the installed Segoe Fluent Icons font contains it. Otherwise they use U+EC19, which is available in Segoe MDL2 Assets. FontIcon uses the theme's Fluent/MDL2 font fallback. Windows 10 does not include Segoe Fluent Icons by default, and older Windows 11 font versions can lack the newer glyph. The app checks the actual glyph once rather than assuming support from the OS version. This does not change the Info buttons' icons or bundle a new font.

The card follows current authentication and account entitlements, not extra network probes. Search uses the same status vocabulary across services; the Info flyouts explain the access method. Public-only playlist URL access is limited. Free Deezer accounts have limited download quality. Qobuz full-track readiness requires explicit streaming rights, not just a member login or a subscription object, and expired/future subscriptions cannot appear ready. A member account without full-track rights can still have green playlist upload access while downloads are red. TIDAL download readiness uses the existing subscription check, with an amber Not checked state when that check is unavailable. Saving credentials, signing in and signing out update the table. Subscription and regional restrictions still apply. The table can scroll horizontally on narrow windows without collapsing its rows.

All four provider buttons say Info and remain available when signed out. Their flyouts show authentication and account details. Spotify and TIDAL each separate Catalog access and User account. TIDAL retains its subscription status, and Deezer/Qobuz retain their existing account-plan details. Account IDs and public app IDs can appear; tokens and secrets do not. Capability descriptions are not repeated in the Spotify or TIDAL flyouts.

## Inline-link locations

- Search: Spotify/TIDAL account-required notices open the provider's Authentication section. Missing download-directory notices open Settings > Downloads > Location.
- Queue: TIDAL account-required download notices open Authentication > TIDAL. Missing download-directory notices open Downloads > Location.
- Upload playlist: signed-out readiness notices for all four providers open their Authentication sections. Provider errors that include the exact Settings path also turn that path into a link.
- Local Explorer lyrics: introduction, empty-list, invalid-directory, completed and cancelled notices open Settings > Downloads > Lyrics. The link is not offered while a lyrics download is active. Using it closes the lyrics dialog first.
- Queue restore: the introductory backup InfoBar opens Settings > General > Backups and closes the restore dialog first. It does not restore, import, export or change queue contents.

FFmpeg and yt-dlp setup-error shortcuts remain future work. Their destination routes exist, but their tool-specific error messages have not been redesigned here.

## Validation

The x64 app and its Settings/dialog XAML compile. Targeted validation passed 188 cases and skipped one opt-in live configuration case. The new cases check Settings routes and invalid internal links, public Spotify imports without user sign-in, private-import guidance with either legacy-toggle value, cancellation and signed-out TIDAL preflight before filesystem writes. Existing playlist and download-policy cases also passed. No live playlists or the running user app were changed. Live OAuth, rendered scrolling/focus and High Contrast still need manual UI verification.

The readiness-table update passed 78 targeted account-state, Qobuz matching and TIDAL download cases, with one opt-in live configuration case skipped. These include Qobuz member accounts with no full-track rights, lossy-only and lossless accounts, expired/future subscriptions, public-only playlist URLs and unverified TIDAL subscriptions. The rendered indicators still need a manual UI check.

The inline-link update compiled the Settings, playlist, queue and lyrics views and passed 213 targeted cases, with one opt-in live configuration case skipped. New route cases cover lyrics, backups and download location, including deduplication when a notice already contains its Settings path. Native hyperlink clicks and dialog-to-Settings navigation still need manual UI verification.

Custom InfoBar content does not inherit the native Message text's insets. Upload playlist, Restore queue, Lyrics and Local Explorer audio conversion now share DialogInfoBarTextStyle for vertical and right-hand spacing. Spotify's profile-visibility note now starts with FluentDL only controls whether this playlist appears on your profile.

The shared style is only for the custom message TextBlocks inside these dialog InfoBars. It is not a global InfoBar style and is not conditional on whether the message contains a link. Ordinary Message properties retain their native template spacing. Floating Search/Queue progress notices are unchanged. The playlist form uses the ContentDialog's built-in scrolling rather than another ScrollViewer inside it. New-playlist fields share one grid; there are no per-field viewport-width bindings.

An earlier isolated Sandbox batch passed five checks and failed the field-alignment check. The narrow ready TIDAL dialog clipped Description and Visibility. Evidence is in C:\Users\derek\AppData\Local\Temp\opencode\dialog-padding-5ebbeba7fab0462888d5bcea93ccc409. The closeout below supersedes that layout result.

## Closeout, 18 October 2026

The dialog now measures its content at a finite width of up to 440 logical pixels and reduces that width when the XamlRoot becomes narrower. All four input controls align with the form's left and right edges. This avoids relying on an unconstrained child's desired width or an initial ScrollViewer viewport value. The built-in ContentDialog scroll area remains responsible for vertical overflow.

The integrated Sandbox build includes open PRs #40, #41 and #42 plus the local playlist changes. Ten notice/layout/navigation checks passed. Native XAML measurements confirmed a width of 440 for Source, Name, Description and Visibility. Reviewed all seven screenshots, including the ready TIDAL form, signed-out Spotify form, a 560-pixel-wide app, Spotify Settings and the three other dialog notices. The dropdown arrow and right corners are visible. Spotify, backup and lyrics inline hyperlinks closed their source dialogs and revealed the intended Settings controls.

Four more Sandbox checks passed for the 800x740 window, toolbar command reachability, keyboard Tab and a Contrast theme. Reviewed all three screenshots. The existing narrow Queue preview pane still leaves little room for card text; this closeout does not redesign that pane. Evidence folders are C:\Users\derek\AppData\Local\Temp\opencode\dialog-padding-15f6e4245a884a978771478be6b478bb and C:\Users\derek\AppData\Local\Temp\opencode\playlist-layout-942451e5e5fc4012b89328d3e497e717. No host app restart or live playlist mutation occurred.
