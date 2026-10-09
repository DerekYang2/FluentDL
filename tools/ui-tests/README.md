# UI tests

FlaUI tests that drive a real FluentDL window through Windows UI Automation. They live in `FluentDL.Characterization.Tests/Ui` (`TestCategory=UI`) and compare what they read with baselines recorded from the pre-refactor app.

They run against a separate **FluentDL (Dev)** package (`DerekYang2.FluentDL.Dev`), which installs next to the real FluentDL with its own empty settings. Your installed FluentDL, its settings, queue and backups are never touched.

## Requirements

- Windows Developer Mode (Settings > System > For developers). It allows registering an unsigned build folder.
- The tests use only UI Automation (setting text, invoking buttons, expanding the source picker), never the real mouse or keyboard, so you can keep using the PC. The dev app's window opens on top during a run, about 1 minute. Some controls, such as the search box's button, are only in the raw view of the automation tree; `UiSteps.FindInRawView` finds them.

## Running

```powershell
# Install the dev app from this checkout and compare with the baselines
.\tools\ui-tests\Run-UiTests.ps1

# Record the baselines from an older commit's app (for example the commit before a refactor phase)
.\tools\ui-tests\Run-UiTests.ps1 -BaselineFrom 2cb77c1
```

Without `FLUENTDL_UI_TESTS=1` (which the script sets), UI tests end as Inconclusive, so a plain `dotnet test` never takes over the desktop.

## Files

| File | Purpose |
|---|---|
| `DevPackage.targets` | Rewrites a copy of `Package.appxmanifest` at build time: package name `DerekYang2.FluentDL.Dev`, display name "FluentDL (Dev)", and its own notification COM class id. Only used when passed as `CustomAfterMicrosoftCommonTargets`. |
| `Install-DevPackage.ps1` | Builds into `FluentDL\bin\uitest` and `obj\uitest`, removes any previous dev package, and registers the new one. Refuses to register a manifest that isn't the dev identity. |
| `Run-UiTests.ps1` | Installs, runs `TestCategory=UI`, and with `-BaselineFrom` records from an older commit in a temporary worktree. |

## Finding control names

`UiProbe` (`TestCategory=UIProbe`) writes the dev app's UI Automation tree to `%TEMP%\opencode\fluentdl-ui-tree.txt`. WinUI uses `x:Name` as the AutomationId, so most named controls in the XAML are directly addressable. Flyouts and ContentDialogs only appear in the tree while open.
