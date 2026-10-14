# UI tests

FlaUI tests that drive a real FluentDL window through Windows UI Automation. They live in `FluentDL.Characterization.Tests/Ui` (`TestCategory=UI`) and compare what they read with baselines recorded from the pre-refactor app.

They run against a separate **FluentDL (Dev)** package (`DerekYang2.FluentDL.Dev`), which installs next to the real FluentDL with its own empty settings. Your installed FluentDL, its settings, queue and backups are never touched.

By default the tests run inside the Windows Sandbox that WinApp CLI manages, so the dev app never opens on your desktop and the dev package installed on your PC is left alone. `-Local` runs them on your desktop instead.

## Requirements

- WinApp CLI 0.7+ and the Windows Sandbox feature, for the default Sandbox runs. The first run in a new Sandbox downloads the .NET SDK into it (about 300 MB, a few minutes). Later runs only copy files that changed.
- Windows Developer Mode (Settings > System > For developers), for `-Local` runs. It allows registering an unsigned build folder. The Sandbox already has it.
- The test accounts in `%USERPROFILE%\.fluentdl-secrets\hurl.secrets` (or `FLUENTDL_SECRETS`). Sandbox runs copy the file into the guest and delete it afterwards.
- Locally, the tests use only UI Automation, never the real mouse or keyboard. The dev app's window opens on top, though, and a few tests read badge colours from the screen, so a window covering the app can fail them. Some controls, such as the search box's button, are only in the raw view of the automation tree; `UiSteps.FindInRawView` finds them.

## Running

```powershell
# In the Sandbox: build, install the dev app there, compare with the baselines
.\tools\ui-tests\Run-UiTests.ps1

# Only the tests whose name contains this; -SkipInstall reuses the last build
.\tools\ui-tests\Run-UiTests.ps1 -Filter PreviewDetails -SkipInstall

# Record the baselines from an older commit's app (for example the commit before a refactor phase)
.\tools\ui-tests\Run-UiTests.ps1 -BaselineFrom 2cb77c1

# On this desktop, as before
.\tools\ui-tests\Run-UiTests.ps1 -Local
```

Sandbox runs copy their results back to `%TEMP%\opencode\sandbox-ui-results`: the `.trx` file, screenshots and failure evidence the tests saved, and any baseline mismatches. Mismatches (`*.actual.json`) are also copied next to the baselines here, as local runs leave them.

Without `FLUENTDL_UI_TESTS=1` (which the scripts set), UI tests end as Inconclusive, so a plain `dotnet test` never takes over the desktop.

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
