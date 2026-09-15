# Installer validation — 2026-09-15

## Artifact

- `artifacts/installer/ExcalidrawDesktop-Setup-0.1.4-x64.exe`
- 296,751,638 bytes (283 MiB), unsigned.
- SHA256: `3311627D8A35ECF954A5FB4E4618B37A9B5BF63C313DA1F41E77D7E8F38D2561`
- Inno Setup 6.7.3; 943 payload files, including 61 dependency notice files.
- Offline WebView2 x64 package SHA256:
  `EBEBC5EC130378FF1AB513F3917BE791A9CF84F849E970B1695FF01801A9D348`.
- Build metadata and checksum sidecars accompany the executable. The build
  records source commit `67601fc1918b600fbf886ac6c9d735d81f2e2661` plus
  `sourceDirty: true`; installer and preceding logging changes were uncommitted.

## Passed checks

- Clean Release publish and Debug build: zero warnings/errors.
- Native service tests: 22 passed, including registration marker detection,
  mutex lifetime, maintenance exclusion, and Windows command-line parsing.
- Standalone Release activation: two files, spaces/Unicode, duplicate focus,
  one surviving process.
- All installer PowerShell scripts parse; `git diff --check` passes.
- `-SkipBuild -Version 0.1.5` correctly rejects the existing 0.1.4 publish.
- Final setup checksum matches its metadata and `.sha256` file.

The real installer smoke suite passed on Windows 11 x64 build 26200 with no
administrator privileges. Its report and logs are in
`artifacts/installer-tests/20260915-051052`:

| Scenario | Result |
| --- | --- |
| Existing nonempty destination | Rejected, exit 7; existing drawing retained |
| Fresh install into path containing spaces and Japanese characters | Passed, exit 0 |
| Self-contained/XAML/web/license payload and marker | Present |
| Installed open command | Executable and `%1` correctly quoted |
| Existing `.excalidraw` default/UserChoice | Preserved |
| New startup during maintenance | Exited without opening the app |
| Setup/uninstall during maintenance | Rejected, exits 7/1 |
| Installed editor startup | Local web content and bridge reached ready state |
| Setup/uninstall with the app open | Rejected, exit 1; running app retained |
| Normal app exit | Closed gracefully |
| Installed file activation | Two tabs, Unicode/spaces preserved, duplicate focused, one process |
| Same-version repair | Passed, exit 0 |
| Obsolete manifest-listed payload | Removed; unlisted user file retained |
| Downgrade from simulated registered version 65534.0.0 | Rejected, exit 7 |
| Uninstall | Passed, exit 0; registration and executable removed |
| User data, user-created install-folder file and default association after uninstall | Preserved |

The successful test installation was removed afterward. No Excalidraw Desktop
process or product uninstall registration remained. The uninstaller intentionally
left the user-created file in its application folder; the test harness then
removed its own temporary test directory.

## Bugs found and fixed

- Redirected plain launch arguments include the executable path. The old app
  treated the complete command line as one drawing path, silently ignoring a
  second file. Native Windows argument parsing now handles the redirected launch.
- Setup needed a shared maintenance gate to prevent app startup during payload
  updates; checking only an already-running app was insufficient.
- WebView2 exit 3010 now stops setup with a restart-required result.
- Inno 6.7.3's signed packed-version comparison sorts major versions above
  32767 incorrectly. Downgrade checks now compare unsigned version components.
- Smoke tests now isolate activation from portable registration, close their
  app gracefully, tolerate delayed WebView2 cleanup, and verify ownership before
  uninstalling a failed test installation.

The requested subagent quality pass reviewed packaging, runtime locking,
associations, upgrade/uninstall safety, dependencies, tests, signing integration,
and documentation. Follow-up reviews found no remaining actionable issues.

## Remaining release validation

This machine already had WebView2 installed. The missing-runtime install,
prerequisite failure/restart, and completely offline clean-machine paths still
need a clean Windows 11 VM. No shared runtime was removed from this machine.

Signing is implemented as an optional build hook but was not exercised with a
real certificate. The manual GitHub Actions workflow has not been run remotely.
Validate both shortcut options and an upgrade from an actual previous installer
release before public distribution; this run exercised repair and simulated
obsolete files/newer registered version. See [Installer](../INSTALLER.md).
