# Public endpoints FluentDL scrapes or calls without credentials.
# Run: hurl --test tools\api\public.hurl

# Spotify embed page (SpotifyApi.GetTrackWebPlayer and GetSpotifyPreviewUrl parse __NEXT_DATA__ and audioPreview)
GET https://open.spotify.com/embed/track/69kOkLUCkxIZYexIgSG8rq
HTTP 200
[Asserts]
body contains "__NEXT_DATA__"
body contains "\"audioPreview\":{\"url\":\""
body contains "\"isExplicit\""

# LRCLIB lyrics (LRCLyricService /get)
GET https://lrclib.net/api/get
[Query]
artist_name: Daft Punk
track_name: Get Lucky
album_name: Random Access Memories
duration: 369
HTTP 200
[Asserts]
jsonpath "$.trackName" isString
jsonpath "$.instrumental" isBoolean
jsonpath "$.plainLyrics" exists
jsonpath "$.syncedLyrics" exists

# LRCLIB search fallback (LRCLyricService /search)
GET https://lrclib.net/api/search
[Query]
track_name: Get Lucky
artist_name: Daft Punk
HTTP 200
[Asserts]
jsonpath "$" count >= 1
jsonpath "$[0].duration" isNumber
