# Qobuz endpoints FluentDL uses (QobuzApi.cs, CustomQobuz\QobuzApi.cs, QobuzApiSharp).
# Run: hurl --test --secrets-file %USERPROFILE%\.fluentdl-secrets\hurl.secrets tools\api\qobuz.hurl

# ISRC lookup (SearchISRC sends the ISRC as a free-text query and takes the first item)
GET https://www.qobuz.com/api.json/0.2/track/search
X-App-Id: {{qobuz_app_id}}
X-User-Auth-Token: {{qobuz_token}}
[Query]
query: USQX91300108
limit: 1
HTTP 200
[Asserts]
jsonpath "$.tracks.items[0].id" == 9140031
jsonpath "$.tracks.items[0].isrc" == "USQX91300108"

# Track search page (ApiSearch<Track>, 28 per chunk in the app)
GET https://www.qobuz.com/api.json/0.2/track/search
X-App-Id: {{qobuz_app_id}}
X-User-Auth-Token: {{qobuz_token}}
[Query]
query: daft punk get lucky
offset: 0
limit: 3
HTTP 200
[Asserts]
jsonpath "$.tracks.items" count == 3
jsonpath "$.tracks.items[0].performers" isString
jsonpath "$.tracks.items[0].album.title" isString

# Search by main artist (AdvancedSearch artist-only uses the #ByMainArtist suffix)
GET https://www.qobuz.com/api.json/0.2/track/search
X-App-Id: {{qobuz_app_id}}
X-User-Auth-Token: {{qobuz_token}}
[Query]
query: Avicii #ByMainArtist
limit: 3
HTTP 200
[Asserts]
jsonpath "$.tracks.items" count >= 1

# Album search (ApiSearch<Album>)
GET https://www.qobuz.com/api.json/0.2/album/search
X-App-Id: {{qobuz_app_id}}
X-User-Auth-Token: {{qobuz_token}}
[Query]
query: random access memories
limit: 2
HTTP 200
[Asserts]
jsonpath "$.albums.items[0].id" isString
jsonpath "$.albums.items[0].artists" exists

# Track by id (GetTrackAsync and UpdateMetadata through QobuzApiSharp)
GET https://www.qobuz.com/api.json/0.2/track/get
X-App-Id: {{qobuz_app_id}}
X-User-Auth-Token: {{qobuz_token}}
[Query]
track_id: 9140031
HTTP 200
[Asserts]
jsonpath "$.title" == "Get Lucky"
jsonpath "$.version" exists
jsonpath "$.isrc" == "USQX91300108"
jsonpath "$.performer.name" isString
jsonpath "$.album.image.large" isString
jsonpath "$.album.genres_list" exists
jsonpath "$.album.upc" isString

# Album by id (GetInternalAlbum)
GET https://www.qobuz.com/api.json/0.2/album/get
X-App-Id: {{qobuz_app_id}}
X-User-Auth-Token: {{qobuz_token}}
[Query]
album_id: 0886443927087
HTTP 200
[Asserts]
jsonpath "$.title" == "Random Access Memories"
jsonpath "$.artist.id" == 36819
jsonpath "$.tracks.items" count == 13
# GetAlbumArtLargest tries mega, then extralarge, then large. Only small, thumbnail, large and back are returned today.
jsonpath "$.image.large" isString
jsonpath "$.image.small" isString

# Label albums (AddTracksFromLabelOrArtist)
GET https://www.qobuz.com/api.json/0.2/label/get
X-App-Id: {{qobuz_app_id}}
X-User-Auth-Token: {{qobuz_token}}
[Query]
label_id: 4592
extra: albums
limit: 2
HTTP 200
[Asserts]
jsonpath "$.albums.items" count >= 1

# Artist albums (AddTracksFromLabelOrArtist)
GET https://www.qobuz.com/api.json/0.2/artist/get
X-App-Id: {{qobuz_app_id}}
X-User-Auth-Token: {{qobuz_token}}
[Query]
artist_id: 36819
extra: albums
limit: 2
HTTP 200
[Asserts]
jsonpath "$.name" == "Daft Punk"
jsonpath "$.albums.items" count >= 1
