# Deezer endpoints FluentDL uses (DeezerApi.cs). Asserts check the fields the parsing code reads.
# Run: hurl --test --secrets-file %USERPROFILE%\.fluentdl-secrets\hurl.secrets tools\api\deezer.hurl

# Track search (GeneralSearch): data[].id
GET https://api.deezer.com/search
[Query]
q: daft punk get lucky
limit: 3
HTTP 200
[Asserts]
jsonpath "$.data" count == 3
jsonpath "$.data[0].id" isInteger
jsonpath "$.data[0].album.title" isString
jsonpath "$.data[0].artist.name" isString

# Album search (GeneralSearch, album mode)
GET https://api.deezer.com/search/album
[Query]
q: random access memories
limit: 2
HTTP 200
[Asserts]
jsonpath "$.data[0].id" isInteger

# Track by id (GetTrack / GetTrackFromJsonElement)
GET https://api.deezer.com/track/67238735
HTTP 200
[Asserts]
jsonpath "$.id" == 67238735
jsonpath "$.title" == "Get Lucky (feat. Pharrell Williams and Nile Rodgers)"
jsonpath "$.isrc" == "USQX91300108"
jsonpath "$.contributors" count >= 1
jsonpath "$.contributors[0].name" isString
jsonpath "$.album.title" == "Random Access Memories"
jsonpath "$.album.cover" isString
jsonpath "$.release_date" isString
jsonpath "$.duration" isInteger
jsonpath "$.track_position" isInteger
jsonpath "$.explicit_lyrics" isBoolean
jsonpath "$.preview" isString
jsonpath "$.link" isString

# Track by ISRC (GetTrackFromISRC)
GET https://api.deezer.com/track/isrc:USQX91300108
HTTP 200
[Asserts]
jsonpath "$.id" == 67238735

# Unknown ISRC returns an error object, which GetTrackFromISRC checks for
GET https://api.deezer.com/track/isrc:XX0000000000
HTTP 200
[Asserts]
jsonpath "$.error" exists

# Album by id (GetAlbum / GetAlbumFromJsonElement, UpdateMetadata)
GET https://api.deezer.com/album/6575789
HTTP 200
[Asserts]
jsonpath "$.title" == "Random Access Memories"
jsonpath "$.upc" == "886443927087"
jsonpath "$.nb_tracks" == 13
jsonpath "$.tracks.data" count == 13
jsonpath "$.genres.data[0].name" isString
jsonpath "$.cover_big" isString
jsonpath "$.cover_xl" isString
jsonpath "$.contributors[0].name" isString

# Album by UPC (GetAlbumFromUPC)
GET https://api.deezer.com/album/upc:886443927087
HTTP 200
[Asserts]
jsonpath "$.id" == 6575789

# ARL login (InitDeezerClient goes through DeezNET's gateway). The gateway needs a body on POST.
POST https://www.deezer.com/ajax/gw-light.php?method=deezer.getUserData&input=3&api_version=1.0&api_token=
Cookie: arl={{deezer_arl}}
{}
HTTP 200
[Asserts]
jsonpath "$.results.USER.USER_ID" exists
jsonpath "$.results.USER.USER_ID" != 0
jsonpath "$.results.OFFER_NAME" isString
jsonpath "$.results.USER.OPTIONS.web_sound_quality.lossless" isBoolean
