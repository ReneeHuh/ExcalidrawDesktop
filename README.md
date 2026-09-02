# Excalidraw Desktop

A packaged WinUI 3 desktop application that hosts the Excalidraw editor locally in WebView2. The editor is consumed as an exact-version npm dependency, so upstream Excalidraw source is not stored in this repository.

## Project structure

```text
ExcalidrawDesktop.App/         WinUI shell, native file lifecycle, and WebView2 host
ExcalidrawDesktop.Core/        Shared protocol, validation, recovery, and workspace logic
ExcalidrawDesktop.Web/         Desktop React entry point and TypeScript bridge
ExcalidrawDesktop.Core.Tests/  C# protocol and domain tests
SmokeTests/                    Packaged application tests
tools/
  Build-Desktop.ps1
  Build-WebAssets.ps1
  Update-Excalidraw.ps1
docs/
  WINUI_DESKTOP_PLAN.md
  validation/
ExcalidrawDesktop.sln
```

Most desktop work belongs in `app`, `tests`, `tools`, or `docs`. The desktop-specific React integration uses only the public `@excalidraw/excalidraw` package API.

## Prerequisites

- Windows 11 x64
- Visual Studio with WinUI application development tools
- Windows SDK 10.0.26100 or newer
- .NET 8 SDK or newer
- Node.js 18 or newer with Corepack
- Evergreen WebView2 Runtime

## Build and run

From the repository root:

```powershell
.\tools\Build-Desktop.ps1
```

The script restores the exact npm dependencies, builds `ExcalidrawDesktop.Web`, copies its offline bundle into `ExcalidrawDesktop.App/Assets/Web`, and builds the native x64 project. `Build-WebAssets.ps1` owns the web bundle generation and can also be run independently.

After the first restore, register and launch the development package with:

```powershell
.\tools\Build-Desktop.ps1 -SkipRestore -Deploy -Launch
```

Do not launch `ExcalidrawDesktop.exe` directly from `bin`; the packaged WinUI application requires its registered MSIX identity.

Open [ExcalidrawDesktop.sln](ExcalidrawDesktop.sln) in Visual Studio when working on the native projects.

## Update Excalidraw

Excalidraw is pinned to an exact version in `ExcalidrawDesktop.Web/package.json`. Update it through the project-owned validation tool:

```powershell
.\tools\Update-Excalidraw.ps1 -Version <version>
```

The tool updates the dependency and lockfile, then runs the desktop web typecheck and tests, the .NET tests, and a complete desktop build. Review and commit the package and lockfile changes only after validation succeeds.

## Tests

Desktop web tests and typecheck:

```powershell
Push-Location .\ExcalidrawDesktop.Web
corepack yarn typecheck
corepack yarn test
Pop-Location
```

Native protocol and document-validation tests:

```powershell
dotnet test .\ExcalidrawDesktop.sln -p:Platform=x64
```

Packaged startup and bridge-readiness smoke test:

```powershell
.\SmokeTests\Invoke-SmokeTest.ps1
```

Packaged two-tab origin and browser-storage isolation test:

```powershell
.\SmokeTests\Invoke-TabbedSmokeTest.ps1 -SkipBuild
```

Packaged workspace restoration and recent-file cleanup test:

```powershell
.\SmokeTests\Invoke-WorkspaceRestoreSmokeTest.ps1 -SkipBuild
```

Forced-termination recovery test:

```powershell
.\SmokeTests\Invoke-RecoverySmokeTest.ps1 -SkipBuild
```

Packaged native title-bar layout and tab-interaction test:

```powershell
.\SmokeTests\Invoke-TitleBarTabsSmokeTest.ps1 -SkipBuild
```

Packaged 1/5/10/20-tab performance report:

```powershell
.\SmokeTests\Invoke-TabPerformanceTest.ps1 -SkipBuild
.\SmokeTests\Invoke-TabPerformanceTest.ps1 -SkipBuild -SuspendInactiveTabs
.\SmokeTests\Invoke-TabPerformanceTest.ps1 -SkipBuild -UnloadInactiveTabs
```

Packaged inactive-tab suspend/resume and full unload/recreate test:

```powershell
.\SmokeTests\Invoke-TabSuspensionSmokeTest.ps1 -SkipBuild
```

Packaged Explorer/file-association and single-instance activation test:

```powershell
.\SmokeTests\Invoke-FileActivationSmokeTest.ps1 -SkipBuild
```

Use `-SkipBuild` to test the registered output or `-KeepRunning` to leave a successful test instance open.

## Current implementation

The desktop app supports native WinUI tabs integrated into the Windows title bar, with a session-bound WebView2 and unique HTTPS origin per drawing. The system caption buttons and a dedicated window drag region remain native. A native File menu provides New Tab, Open, Recent, Save, Save As, Save All, Close Tab, Close Window, Settings, and Exit commands with global shortcut handling. The title-bar gear shows and focuses a singleton native Settings tab built with Windows Community Toolkit `SettingsCard` controls; it never creates a WebView2 or document session, and closing the tab only hides it until the gear is clicked again. Its persisted options cover the app-frame theme, reopening saved tabs, inactive-tab suspension, and full clean-editor unloading. Clean, safe inactive tabs sleep after five minutes, fully unload their WebView after fifteen minutes, and recreate the editor when selected; dirty, loading, saving, recovering, and conflicted tabs remain live. A clean file is validated and cached before unloading so disk changes cannot silently replace the displayed drawing. Recreating a clean editor intentionally resets its in-memory undo history. Tabs can be reordered, middle-clicked to close, and managed through a context menu with Copy Path, Reveal in Explorer, Close, Close Others, and Close Tabs to the Right commands with per-drawing save protection. A bottom desktop status bar in each editor shows its path, saved or unsaved state, recovery time, suspension, and external-file conflicts. Windows registers `.excalidraw` files with the app; Explorer activation, taskbar jump-list entries, and multi-file drag/drop open files as tabs in the single running instance and focus an existing tab for duplicates. Closing the window offers Save All, Discard All, Review Tabs, and Cancel; Save All stops safely if any drawing cannot be saved. Open reuses a clean untitled tab or creates a new tab, and focuses an existing tab when the selected file is already open. Clean file-backed tabs, their order, and the active tab restore at startup, while the native Recent submenu and Windows taskbar keep up to ten valid recent paths. Dirty tabs write separate atomic recovery snapshots after two seconds of inactivity and restore as dirty after forced termination; confirmed saves and explicit discards remove those snapshots. Modified, moved, or deleted backing files block normal Save and offer Reload or Locate, Save As, and Keep Editing. Native-backed Save and Save As remain isolated to the owning tab, including protection against choosing a path owned by another tab. Browser-backed document actions are disabled so every document path goes through the native service. See [the WinUI plan](docs/WINUI_DESKTOP_PLAN.md) and [validation checklists](docs/validation) for remaining acceptance work.

The next Phase T4 work is the manual accessibility, input, scaling, and theme validation for native title-bar tabs, as described in [the tabbed desktop plan](docs/TABBED_DESKTOP_PLAN.md).

The longer-term desktop direction is captured in the [future product roadmap](docs/FUTURE_DESKTOP_ROADMAP.md).

The canonical checklist of all remaining work is maintained in the
[desktop backlog](docs/DESKTOP_BACKLOG.md).
