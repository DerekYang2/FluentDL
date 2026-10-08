# API checks

[Hurl](https://hurl.dev) files that call the endpoints FluentDL uses and check that the response fields the app's parsing code reads are still there. Run them when a service seems to have changed, or before recording new characterization baselines.

```powershell
# Install once
winget install Orange-OpenSource.Hurl

# Build the secrets file from a FluentDL settings export (see below), then:
hurl --test --secrets-file $HOME\.fluentdl-secrets\hurl.secrets tools\api\*.hurl
```

| File | Covers | Needs |
|---|---|---|
| `deezer.hurl` | search, track, ISRC, album, UPC, ARL login | `deezer_arl` |
| `qobuz.hurl` | ISRC search, track and album search, track, album, label, artist | `qobuz_app_id`, `qobuz_token` |
| `public.hurl` | Spotify embed page, LRCLIB lyrics | nothing |

## Secrets

The files contain only `{{variable}}` references. Values come from a `NAME=VALUE` file outside the repo, passed with `--secrets-file`, so Hurl hides them in its logs. One way to make it: export settings from FluentDL (Settings, Export) to `%USERPROFILE%\.fluentdl-secrets\FluentDL_Settings.json`, then write one line per key from that file. Each value in the export is a JSON-encoded string, so decode it twice.

Never commit the secrets file or paste its values into an issue.

## Notes

- Hurl writes its summary to stderr, so PowerShell may report an error even when every request passed. Check `$LASTEXITCODE`, which is 0 on success.
- Deezer's gateway (`gw-light.php`) returns 411 on a POST with no body, so the login check sends `{}`.
