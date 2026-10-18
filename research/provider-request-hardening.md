# Catalog request handling

## Scope

Conversion lookups share a request budget for each provider. TIDAL catalog GETs use `ProviderRequests.Tidal`, Deezer's public catalog uses `ProviderRequests.Deezer`, and Qobuz conversion searches and track/album metadata use `ProviderRequests.Qobuz`.

These defaults limit each provider to two in-flight requests and space request starts by 200 ms. They are conservative application settings, not documented provider quotas. Increasing Conversion threads does not bypass these provider budgets. Providers have independent budgets, so one provider's cooldown does not pause another.

HTTP 429 pauses other catalog workers for that provider. Both numeric and HTTP-date `Retry-After` values work. Missing headers use exponential backoff with up to 25% jitter. Past dates become zero delay, but request pacing still applies. A final rate-limited response still updates the shared cooldown, even when that lookup has exhausted its retries.

TIDAL and Qobuz permit three rate-limit retries. Deezer permits five, preserving the existing quota retry budget. Transport failures, timeouts, HTTP 408, 500, 502, 503 and 504 permit two transient retries. Retry waits do not occupy an active request slot. Each HTTP attempt has a 30-second deadline. User cancellation stops queued work, cooldown waits and active requests without retrying it. An exhausted transport timeout is reported as a timeout, not mistaken for user cancellation.

Deezer also reports quota limits in JSON, sometimes with a successful HTTP status. The existing `error.code = 4` and `Quota limit exceeded` message are recognized. A track title containing that phrase does not trigger retries. Its default transport timeout is now 30 seconds instead of 5 seconds.

Permanent 400/401/403/404 responses are not retried by this policy. TIDAL keeps its existing separate authentication-refresh logic. Every transport retry constructs a fresh request message and disposes rejected HTTP responses.

## Qobuz conversion availability

Qobuz search can return withdrawn track IDs with matching ISRCs but `streamable=false` and `displayable=false`. An audit of the 28 omissions in the 812-track queue found HTTP 404 for every missing ID. Sampled ISRC searches still returned these unavailable IDs. The audit only read the playlist and catalog.

Conversion now requires a positive track ID and explicit `streamable=true` and `displayable=true`. ISRC matching skips unavailable results and continues through the returned exact matches. Metadata matching checks the full track response for album candidates and the availability fields for artist/title search results. Missing availability fields do not count as confirmation. If no available candidate matches, conversion reports no match instead of green or yellow success. This does not change general browsing, direct catalog lookup or existing queue rows.

Artist/title fallback now requests the first 28 results. Its previous positional argument set an offset of 28 and skipped that first page. All conversion requests still use the provider budget and caller cancellation token.

Validation passed 124 targeted offline availability, request-policy, playlist-writer and dialog cases, plus seven read-only live ISRC cases. The live checks include the withdrawn IDs' ISRCs and an available recording as a positive control. No queue or playlist mutations were part of this validation.

## Deliberate limits

- This change covers catalog conversion traffic. It does not apply these retries to playlist mutations, authentication POSTs, audio/CDN transfers or opaque requests inside third-party download SDKs.
- Playlist create/append operations retain their existing no-replay and verification rules. A timeout on a write must not create another playlist or duplicate its tracks.
- Qobuz's conversion fallback album search and track/album detail requests now use its governed HTTP client instead of synchronous SDK calls. Other older SDK-based browsing/signing methods are outside this change.
- Spotify retains its existing SDK retry handler. A cross-service ISRC search can still contact Spotify before the target lookup. This does not claim to solve every provider's request path.
- There is no new cross-track result cache or new conversion result category. Lookups still fail after their bounded retry budget. Existing matchers may present an exhausted request as a failed/no-match conversion.
- The ETA remains an average-progress estimate, not a retry countdown. Catalog conversion updates it when a row finishes; download progress updates roughly every 100 ms.
- ETA text includes seconds below one hour, such as `Estimated time: 2 min 54 sec`. Hour-scale estimates retain hours and minutes only. Values round up to a whole second, and the estimate still hides below five seconds.

## Protocol evidence

- HTTP 429 and optional `Retry-After` behavior: [MDN 429](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Status/429) and [Retry-After](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Retry-After).
- Deezer's existing client and local regression fixture already recognize JSON quota error 4. Its [official error reference](https://developers.deezer.com/api/errors) requires developer login, so this change does not claim a verified public numeric quota or guarantee that Deezer sends `Retry-After`.
- No official public Qobuz numeric quota or `Retry-After` guarantee was verified. Its HTTP 429/header handling is defensive. Generated third-party rate-limit tables are not used as authoritative limits.

Validation uses fake HTTP handlers and local loopback servers. It does not deliberately flood live providers to discover their quotas.
