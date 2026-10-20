# UI tests

FlaUI tests that drive a real FluentDL window through Windows UI Automation. They live in `FluentDL.Characterization.Tests/Ui` (`TestCategory=UI`) and compare what they read with baselines recorded from the pre-refactor app.

They run against a separate **FluentDL (Dev)** package (`DerekYang2.FluentDL.Dev`), which installs next to the real FluentDL with its own empty settings. Your installed FluentDL, its settings, queue and backups are never touched.

By default the tests run inside the Windows Sandbox that WinApp CLI manages, so the dev app never opens on your desktop and the dev package installed on your PC is left alone. `-Local` runs them on your desktop instead.

## Album expansion checks

`Test-QueueAlbumExpansion.ps1` tests the standard and compact expand buttons, confirmation cancellation, duplicate skipping and preservation of an incomplete album. It uses synthetic cached YouTube album tracks, not provider credentials or audio downloads.

Build and launch the app with `winapp run FluentDL/FluentDL.csproj --on sandbox --arch x64 --detach --json`. Pass `CustomAfterMicrosoftCommonTargets` as the absolute path to `tools/ui-tests/AlbumExpansionPreview.targets`, with separate `IntermediateOutputPath=obj\album-expansion-ui\` and `OutputPath=bin\album-expansion-ui\` properties. The target gives this build the separate `DerekYang2.FluentDL.AlbumExpansionTest` identity.

Copy `sandbox/Set-AlbumExpansionFixture.ps1` into the guest with `winapp target push`. Run it through `winapp target exec sandbox` with the reported guest process ID. It closes only the isolated test app, refuses to overwrite an existing queue, seeds the fixture and relaunches the app. Use its new process ID with `Test-QueueAlbumExpansion.ps1 -AppPid <guest-process-id>`. Its `-ReadOnly` mode returns saved queue rows for persistence checks. Do not use the seed helper against a normal installation.

## Requirements

- WinApp CLI 0.7+ and the Windows Sandbox feature, for the default Sandbox runs. The first run in a new Sandbox downloads the .NET SDK into it (about 300 MB). Later runs only copy files that changed. The guest script first turns off Smart App Control inside the Sandbox, Microsoft's workaround for very slow installs there ([Windows-Sandbox#68](https://github.com/microsoft/Windows-Sandbox/issues/68)). It never changes this PC.
- Windows Developer Mode (Settings > System > For developers), for `-Local` runs. It allows registering an unsigned build folder. The Sandbox already has it.
- The test accounts in `%USERPROFILE%\.fluentdl-secrets\hurl.secrets` (or `FLUENTDL_SECRETS`). Sandbox runs copy the file into the guest and delete it afterwards.
- Locally, the tests use only UI Automation, never the real mouse or keyboard. The dev app's window opens on top, though, and a few tests read badge colours from the screen, so a window covering the app can fail them. Some controls, such as the search box's button, are only in the raw view of the automation tree; `UiSteps.FindInRawView` finds them.

## Running

```powershell
# In the Sandbox: build, install the dev app there, compare with the baselines
.\tools\ui-tests\Run-UiTests.ps1

# Only the tests whose name contains this; -SkipInstall reuses the last build
.\tools\ui-tests\Run-UiTests.ps1 -Filter PreviewDetails -SkipInstall

# Queue workflows, using TIDAL, Qobuz and Spotify web player mode
.\tools\ui-tests\Run-UiTests.ps1 -Filter CatalogWorkflowUiTests

# Shared lifecycle workflows, represented by Qobuz
.\tools\ui-tests\Run-UiTests.ps1 -Filter LifecycleWorkflowUiTests

# Also test a converted Spotify album with the official API's local test credentials
.\tools\ui-tests\Run-UiTests.ps1 -Filter CatalogWorkflowUiTests -IncludeSpotifyOfficial

# Also test one fixed YouTube track link, without a download or a broad search
.\tools\ui-tests\Run-UiTests.ps1 -Filter CatalogWorkflowUiTests -IncludeYoutube

# Record the baselines from an older commit's app (for example the commit before a refactor phase)
.\tools\ui-tests\Run-UiTests.ps1 -BaselineFrom 2cb77c1

# On this desktop, as before
.\tools\ui-tests\Run-UiTests.ps1 -Local
```

Sandbox runs copy their results back to `%TEMP%\opencode\sandbox-ui-results`: the `.trx` file, screenshots and failure evidence the tests saved, and any baseline mismatches. Mismatches (`*.actual.json`) are also copied next to the baselines here, as local runs leave them.

Each run gets its own timestamped subdirectory so an open screenshot cannot block a later result transfer. Builds disable reusable build servers to avoid leaving redirected output handles open after the build finishes.

Without `FLUENTDL_UI_TESTS=1` (which the scripts set), UI tests end as Inconclusive, so a plain `dotnet test` never takes over the desktop.

## Workflow coverage

`CatalogWorkflowUiTests` exercises the transitions that individual Search snapshots do not cover:

- A mixed queue with a TIDAL track, Qobuz album and Spotify web-player track. Convert the TIDAL track to Spotify, then the Qobuz album to TIDAL. Preview every row after each conversion. Clear the queue through the UI, restore its automatic backup, and preview again. Restart and preview the restored queue in compact mode. Check identity, album/track kind and ordering against the guest database.
- With `-IncludeSpotifyOfficial`, convert a Qobuz album to Spotify using the official API, preview its actual track list, restart, and preview it again. This catches the dropped album fields that caused converted Spotify album previews to crash. It requires `spotify_client_id` and `spotify_client_secret` in the local secrets file. Missing credentials skip it; a failed conversion or crash does not count as a pass.
- With `-IncludeYoutube`, queue one fixed YouTube track link, convert it to Qobuz, preview it, restart and preview again. No album scanning, repeated queries or YouTube downloads. It has a three-minute timeout and no retry loop. It stays outside default runs because YouTube can bot-check the connection.

These workflows only run in the Sandbox, use local test credentials, and never sign in for TIDAL downloads. They do not need GitHub repository secrets. They save screenshots after previews under `artifacts\workflow-ui` in the pulled result folder. They do not repeat the existing queue-download or metadata-baseline tests.

`LifecycleWorkflowUiTests` uses Qobuz for source-independent behavior:

- Export a track and album through the Save picker, clear the queue, import the ZIP through the Open picker, restart, and preview the restored rows. Check their order and album/track identity.
- Stop a single-threaded queue download, restart, and retry the unfinished rows. Check that every input becomes one local row, without duplicates.
- Create one file that collides with an album track's output filename while overwrite is off. Expect twelve successful local rows and one retryable streaming row. Restart, remove that one blocker, retry, and expect thirteen local rows. This is a controlled failure, not a dependency on an intermittent service error.
- Download a track and an album from Search. Check the completion message, files, hidden progress bar and Local Explorer results. The existing album test covers the separate Queue download route.

The download workflows use MP3 and work with the free test account's samples. They test application behavior, not subscription entitlement or lossless audio. Preview checks wait for the known fixture artwork to render as well as for the metadata and album track list. They do not verify audible playback.

Every `DevApp` session now fails if the app exits unexpectedly or writes a new fatal log entry. Old fatal entries do not fail a later session. The check does not print log contents, which may contain account information.

### Current validation

The combined Sandbox run on 2026-10-16 passed 16 UI cases, including the optional official Spotify and YouTube tests. One diagnostic WebView2 test was intentionally skipped. The run included all seven new workflow cases alongside the existing UI tests and took about five minutes of test execution.

ZIP export succeeds, but the export/import workflow is not verified end to end yet: entering the filename in the Open picker still fails. It has `TestCategory=Deferred` and the runner excludes that category. Remove that category when the picker automation is fixed; do not count the excluded test as a pass.

The Search download cases also check Local Explorer's count on its first visit, after navigating back, and after Clear. They reproduced the stale "No results" label before the constructor's redundant zero-count reset was removed, and pass with that fix. Clearing the list must not delete the downloaded files.

## Files

| File | Purpose |
|---|---|
| `DevPackage.targets` | Rewrites a copy of `Package.appxmanifest` at build time: package name `DerekYang2.FluentDL.Dev`, display name "FluentDL (Dev)", and its own notification COM class id. Only used when passed as `CustomAfterMicrosoftCommonTargets`. |
| `Install-DevPackage.ps1` | Builds into `FluentDL\bin\uitest` and `obj\uitest`, removes any previous dev package, and registers the new one. Refuses to register a manifest that isn't the dev identity. Used by `-Local` runs. |
| `Run-UiTests.ps1` | Runs `TestCategory=UI` in the Sandbox or, with `-Local`, here. With `-BaselineFrom` it records from an older commit in a temporary worktree. |
| `sandbox\Invoke-SandboxUiTests.ps1` | Builds here, copies the dev package, test binaries, baselines, Windows App SDK runtime and secrets into the Sandbox, runs the guest script, pulls the results back, and deletes the secrets from the guest. |
| `sandbox\Run-InGuest.ps1` | Runs in the Sandbox: installs the .NET SDK and Windows App SDK runtime the first time, registers a fresh dev package, and runs the tests. |

## Finding control names

`UiProbe` (`TestCategory=UIProbe`) writes the dev app's UI Automation tree to `%TEMP%\opencode\fluentdl-ui-tree.txt`. WinUI uses `x:Name` as the AutomationId, so most named controls in the XAML are directly addressable. Flyouts and ContentDialogs only appear in the tree while open.

## Spectrogram integration smoke test

`Test-Spectrogram.ps1` checks the Spek.NET dialog with a generated local stereo FLAC. It requires WinApp CLI 0.7+ and no music-service credentials. Start a dev package in Sandbox, push a fixture with title `Spectrogram sample` into `C:\WinApp\work\SpekFixtures\spectrogram.flac`, then pass the guest PID and target together:

```powershell
pwsh .\tools\ui-tests\Test-Spectrogram.ps1 -Target sandbox -AppPid <guestPID> `
    -FixturePath C:\WinApp\work\SpekFixtures\spectrogram.flac `
    -ArtifactDirectory "$env:TEMP\opencode\spectrogram-results"
```

The script checks opening from Local Explorer, changing the palette, FFT size and channel, zoom/reset, selection, inspection options, PNG export, closing/reopening, and both queue layouts. Results and screenshots go to the chosen artifact folder. Review the screenshots as well as the assertions.

The dialog uses the published Spek.NET.WinUI 0.3.0-preview.1 package. Its title stays left, with a right-aligned wrapping group of buttons and centered button labels. MP3 references, spectrum profile, cutoff marker and Reset zoom controls are below that header. The leftmost zoom percentage precedes fixed-width time, frequency and level fields. A two-axis minimap appears above 100% zoom and shows a progress ring during refinement. Initial analysis and completed refinement have no progress/ready text. The spectrum caption includes the selected passage's times or whole-track context without a duplicate footer row. Clear selection appears only while the profile is enabled and a time passage is selected. It restores whole-track inspection without changing zoom. Turning the profile off also clears the passage and its highlight. The dB fields use inline spinners rather than floating spinner popups.

Matching axes show one zoom percentage, such as `Zoom: 150%`; different axes show `X: 150% / Y: 100%`. The minimap and readout keep separate space, including when Clear selection appears. Ctrl+scroll zooms time and frequency around the pointer; +/- still zooms around the center but is not advertised in the hint row. After a 350 ms pause, the control refines the visible time range in the background without changing the FFT size. Whole-file and light zoom views keep fast analysis. At 4× time zoom, refinement uses the detailed file-anchored grid. Reset zoom restores both axes and the cached overview. Save exports the current image without waiting for pending refinement.
