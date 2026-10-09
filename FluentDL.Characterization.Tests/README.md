# FluentDL.Characterization.Tests

These tests call the real Deezer, Qobuz, Spotify and YouTube code in the app project and compare the results with JSON baselines in `Baselines/`. They pin the app's current behavior, bugs included, so a refactor that changes behavior fails a test.

## Running

```powershell
# Offline tests only (no network, a few seconds)
dotnet test FluentDL.Characterization.Tests -p:Platform=x64 --filter "TestCategory!=Live&TestCategory!=UI&TestCategory!=UIProbe"

# Live tests in parallel processes, skipping everything that calls YouTube and the Spotify official search (about 2 minutes)
tools\live-tests\Run-LiveTests.ps1 -Quick

# All live tests in parallel. Run this before committing a phase.
tools\live-tests\Run-LiveTests.ps1
```

The source classes keep their state in static fields, so one process runs its tests one at a time. The runner starts one process per group of sources instead. The YouTube-heavy tests stay in one group, so YouTube sees a single client. Each group's full output goes to `%TEMP%\opencode\fluentdl-live-tests`.

The live tests call the sources through the same adapters the app registers (`Services/Sources`). `TestSources` gives them an in-memory settings store filled from the secrets file, so `ConnectAsync` signs in with the test credentials.

Live tests read credentials from `%USERPROFILE%\.fluentdl-secrets\hurl.secrets` (`NAME=VALUE` lines). Set `FLUENTDL_SECRETS` to use another path. A test whose credentials are missing ends as Inconclusive.

The project is not built by the solution. Run it with `dotnet test` when you want it.

## When a test fails

The test writes `Baselines/<name>.actual.json` next to the baseline and prints the first differing line. Compare the two files.

- If the change is a regression, fix the code.
- If the change is intended (a bug fix, for example), re-record that baseline and commit it in the same change.
- If the service itself changed (a catalog update or a new search ranking), re-record the baselines from `master`, not from your branch, and commit them on their own.

Live results can wobble from one request to the next, so `Snapshot.MatchLive` produces each result up to 3 times and passes on the first match.

## Recording baselines

```powershell
$env:FLUENTDL_RECORD = '1'
dotnet test FluentDL.Characterization.Tests -p:Platform=x64
Remove-Item Env:FLUENTDL_RECORD
```

## How it loads the app

- The app's `FluentDL.dll` normally runs a Windows App SDK deployment check when it loads, and that check needs MSIX package identity, which a test host does not have. The project reference builds a separate copy of the app with `WindowsAppSdkDeploymentManagerInitialize=false` into `FluentDL\bin\characterization` and `FluentDL\obj\characterization`. The normal app build is untouched.
- `FluentDL.csproj` has `InternalsVisibleTo` for this project, because the source classes are `internal`.
- Spotify runs in web player mode. `LiveSources` builds the web player services directly and sets `SpotifyApi`'s private fields, because the app's `SpotifyApi.Initialize` gets them from `App.GetService`.

See the "FluentDL source refactor plan" notes for the fixtures and the list of known bugs that the baselines pin.
