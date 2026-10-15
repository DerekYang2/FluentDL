# Spotify official Web API (SpotifyApi with a client id and secret).
# Run: hurl --test --secrets-file %USERPROFILE%\.fluentdl-secrets\hurl.secrets tools\api\spotify.hurl

# Client credentials token (ClientCredentialsAuthenticator in SpotifyAPI.Web)
POST https://accounts.spotify.com/api/token
[BasicAuth]
{{spotify_client_id}}: {{spotify_client_secret}}
[FormParams]
grant_type: client_credentials
HTTP 200
[Captures]
spotify_token: jsonpath "$.access_token"
[Asserts]
jsonpath "$.token_type" == "Bearer"

# ISRC search (SpotifyApi.GetTrackFromISRC searches "isrc:" and checks external_ids.isrc)
GET https://api.spotify.com/v1/search
Authorization: Bearer {{spotify_token}}
[Query]
type: track
q: isrc:USQX91300108
HTTP 200
[Asserts]
jsonpath "$.tracks.items" count >= 1
jsonpath "$.tracks.items[0].external_ids.isrc" == "USQX91300108"

# Track by id (SpotifyApi.GetTrack, UpdateMetadata)
GET https://api.spotify.com/v1/tracks/69kOkLUCkxIZYexIgSG8rq
Authorization: Bearer {{spotify_token}}
HTTP 200
[Asserts]
jsonpath "$.name" contains "Get Lucky"
jsonpath "$.album.images" count >= 2
jsonpath "$.external_ids.isrc" == "USQX91300108"

# Album by id (SpotifyApi.GetAlbum, album links)
GET https://api.spotify.com/v1/albums/4m2880jivSbbyEGAKfITCa
Authorization: Bearer {{spotify_token}}
HTTP 200
[Asserts]
jsonpath "$.name" == "Random Access Memories"
jsonpath "$.external_ids.upc" exists
jsonpath "$.tracks.items" count >= 1
