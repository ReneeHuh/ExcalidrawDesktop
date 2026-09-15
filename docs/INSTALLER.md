# Windows installer

The installer follows ReadPlease's Inno Setup approach: one versioned setup
executable, a per-user installation, a Start menu entry, an optional desktop
shortcut, and an optional launch at the end. It targets Windows 11 x64
(build 22000 or newer). ARM64 is not supported by this package.

## Build

Run from the repository root in **PowerShell 7** with the normal desktop build
requirements (.NET SDK, Windows SDK, Node 22 and Corepack):

```powershell
./installer/build-installer.ps1
```

The command restores dependencies, builds the local web bundle, creates a clean
self-contained Release publish, and compiles setup. Output is ignored by Git:

```text
artifacts/installer/ExcalidrawDesktop-Setup-0.1.5-x64.exe
artifacts/installer/ExcalidrawDesktop-Setup-0.1.5-x64.exe.sha256
artifacts/installer/ExcalidrawDesktop-Setup-0.1.5-x64.exe.build.json
```

Version defaults to `ExcalidrawDesktop.App.csproj`. Use `-Version 0.1.6` to
override the app, file, assembly, and installer versions together. Three numeric
components are required. `-SkipBuild` packages the existing Release publish and
rejects a version mismatch; use it only after building the current source.

Inno Setup 6.7.3 and the offline Microsoft WebView2 Evergreen x64 installer are
downloaded into `artifacts/installer-tools`. Both downloads are pinned by SHA256
and checked for valid publisher signatures. Inno Setup's compiler version is
also checked during compilation. The dependency helper installs Inno Setup
silently for the current user when no compiler is supplied. It never installs
WebView2 on the build machine.

Existing dependencies can be supplied explicitly:

```powershell
./installer/build-installer.ps1 `
  -IsccPath 'C:\Tools\Inno Setup 6\ISCC.exe' `
  -WebView2InstallerPath 'C:\Tools\MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
```

The supplied WebView2 file must match `installer/dependencies.json`, including
its architecture-specific hash. To refresh it, obtain the x64 standalone
download from the recorded official discovery URL, verify Microsoft's signature,
and update its resolved download URL and SHA256 together. The runtime remains
Evergreen and receives Microsoft's normal updates after installation. Build
metadata records the embedded setup package hash and updater executable version;
that updater version is not the browser engine version.

The payload includes compiled XAML/PRI files, local web assets, .NET and Windows
App SDK runtimes, project notices, npm license notices, and restored NuGet
license/notice metadata. Debug symbols and smoke-test data are excluded.

## Installation and upgrades

- Default application folder: `%LOCALAPPDATA%\Programs\ExcalidrawDesktop`.
- Existing data folder: `%LOCALAPPDATA%\ExcalidrawDesktop`.
- No administrator rights are requested. The offline WebView2 installer runs
  only when neither a per-machine nor per-user runtime is registered.
- A WebView2 failure or required restart stops setup before application files
  are copied. After restarting, run setup again.
- `identity.json` is shared with the native app. Keep its AppId, marker, ProgID,
  and mutex prefixes stable across releases.
- Setup and uninstall require the app to be closed. A shared maintenance gate
  prevents new app instances from starting while payload files change. Drawings
  are never closed automatically by setup.
- A newer setup upgrades in place. The same version repairs missing files.
  Older versions are rejected until the newer installation is uninstalled.
- Changing an existing installation's location requires uninstalling it first.
  A new destination must be empty; user-data folders and junctions are rejected.
- Upgrades compare the old and new `installed-files.txt` manifests and remove
  obsolete listed application files only. Cleanup rejects traversal, reparse
  points and drawing paths. Unlisted files are retained.
- Uninstall removes installer-owned files and associations, preserves user data,
  and leaves WebView2 installed because it is shared with other apps.

Setup registers `ExcalidrawDesktop.Drawing` as an **Open with** candidate and a
Windows Registered Applications entry. It does not set the extension default or
write `UserChoice`. The installed app recognizes its marker and skips portable
registration. The ordinary publish folder contains no marker and retains its
portable registration behavior. Old portable registrations are left alone.

For unattended installation:

```powershell
./ExcalidrawDesktop-Setup-0.1.5-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="setup.log"
```

Silent setup exits unsuccessfully if the app is open, maintenance is in
progress, or a prerequisite fails. Setup logs to the user's temporary directory
by default; `/LOG` allows an explicit location.

## Signing and CI

Local builds are unsigned unless `-SigningScriptPath` is provided. Pass a trusted
PowerShell script accepting `-FilePath` that signs and timestamps that file and
returns a nonzero exit code on failure. The build signs the app's own EXE/DLLs,
the Inno uninstaller and setup, and verifies the resulting Authenticode signatures.
Use the same publisher identity for subsequent releases.

```powershell
./installer/build-installer.ps1 -SigningScriptPath 'C:\ReleaseTools\Sign-File.ps1'
```

No certificate, password, or signing secret is stored in the repository.
The manual [Windows installer workflow](../.github/workflows/installer.yml)
builds an unsigned Actions artifact with its checksum and metadata. It does not
publish a public release. Download its artifact after a successful run.

## Validation

```powershell
dotnet test ExcalidrawDesktop.Native.Tests --configuration Release
./SmokeTests/Invoke-InstallerSmokeTest.ps1 `
  -InstallerPath ./artifacts/installer/ExcalidrawDesktop-Setup-0.1.5-x64.exe
```

Run the installer smoke test in an interactive Windows test account. It uses
temporary install/data folders and refuses an existing product registration.
It checks install/repair, payloads, running-app and maintenance guards, downgrade
rejection, normal installed startup, multiple drawing activation with spaces and
Unicode, duplicate focus, obsolete file cleanup, association/default handling,
and uninstall preservation. Logs go to `artifacts/installer-tests`.

Before public distribution, also validate in a clean Windows 11 VM with no .NET,
Windows App SDK or WebView2 runtime: disconnect networking, install, open/edit/
save a drawing, restart when requested, and uninstall. Test a real previous
release upgrade with recovery data, both shortcut options, and signed setup when
a signing certificate is available. These checks cannot be replaced by testing
on a development machine with runtimes already installed.

References: [Inno Setup](https://jrsoftware.org/isdl.php),
[AppMutex](https://jrsoftware.org/ishelp/topic_setup_appmutex.htm),
[setup events](https://jrsoftware.org/ishelp/topic_scriptevents.htm),
[WebView2 distribution](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).
