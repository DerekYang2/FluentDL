# TIDAL official API (planned TidalSource, Phase 8). Client credentials, no subscription needed.
# Run: hurl --test --secrets-file %USERPROFILE%\.fluentdl-secrets\hurl.secrets tools\api\tidal.hurl

POST https://auth.tidal.com/v1/oauth2/token
[BasicAuth]
{{tidal_client_id}}: {{tidal_client_secret}}
[FormParams]
grant_type: client_credentials
HTTP 200
[Captures]
tidal_token: jsonpath "$.access_token"
[Asserts]
jsonpath "$.access_token" isString
jsonpath "$.expires_in" isInteger

# Track by ISRC
GET https://openapi.tidal.com/v2/tracks
Authorization: Bearer {{tidal_token}}
Accept: application/vnd.api+json
[Query]
filter[isrc]: USQX91300108
countryCode: US
HTTP 200
[Asserts]
jsonpath "$.data" count >= 1
jsonpath "$.data[0].type" == "tracks"
jsonpath "$.data[0].attributes.isrc" == "USQX91300108"

# Album by UPC
GET https://openapi.tidal.com/v2/albums
Authorization: Bearer {{tidal_token}}
Accept: application/vnd.api+json
[Query]
filter[barcodeId]: 886443927087
countryCode: US
HTTP 200
[Asserts]
jsonpath "$.data" count >= 1
jsonpath "$.data[0].attributes.title" == "Random Access Memories"
