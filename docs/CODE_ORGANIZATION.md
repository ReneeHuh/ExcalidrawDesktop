# Code organization

The native UI uses feature folders inspired by ReadPlease. The application,
shared logic, and browser editor remain separate projects because they run in
different environments and have different lifetimes.

## Find the code for a change

| Change | Start here |
| --- | --- |
| Application startup, activation, diagnostics | `ExcalidrawDesktop.App/App.xaml.cs` |
| Window construction and lifetime | `ExcalidrawDesktop.App/MainWindow.xaml.cs` |
| Window title, menu availability, active-tab presentation | `ExcalidrawDesktop.App/ViewModels/MainViewModel.cs` |
| Settings controls and preference bindings | `ExcalidrawDesktop.App/Views/Settings/` |
| Tab headers, status text, native tab interactions | `ExcalidrawDesktop.App/Views/Workspace/` |
| WebView control and failure overlay | `ExcalidrawDesktop.App/Views/Workspace/UserControls/` |
| Live editor resources and transfer identity | `ExcalidrawDesktop.App/Sessions/` |
| Open/save, recovery integration, export | `ExcalidrawDesktop.App/Services/Documents/` |
| WebView lifecycle and native bridge dispatch | `ExcalidrawDesktop.App/Services/Editor/` |
| Shared settings, windows, close coordination, modal ownership | `ExcalidrawDesktop.App/Services/Workspace/` |
| Windows resources, settings storage, paths, diagnostics | `ExcalidrawDesktop.App/Services/Platform/` |
| Protocol, validation, persistence and recovery rules | `ExcalidrawDesktop.Core/` |
| Native smoke scenarios and fixtures | `ExcalidrawDesktop.App/Testing/` |
| Desktop smoke-test launchers | `SmokeTests/` |
| Browser startup | `ExcalidrawDesktop.Web/src/main.tsx` |
| Editor rendering and document orchestration | `ExcalidrawDesktop.Web/src/DesktopApp.tsx` |
| Browser library, export, and smoke subscriptions | `ExcalidrawDesktop.Web/src/document/useEditorLibrary.ts`, `export/useImageExport.ts`, `testing/useDesktopSmoke.ts` |

## Ownership and lifetimes

- **Application:** `App` creates one `ApplicationWorkspaceCoordinator`. It owns
  shared preferences, recent files, recovery storage, library storage, path
  reservations, and coordination between windows. Window view models do not
  create replacements for these shared services.
- **Window:** each `MainWindow` owns one long-lived `MainViewModel`, which owns
  its `SettingsVM` and exposes the active tab and available menu actions. The
  window also owns its document, editor, export, and close controllers.
- **Tab:** `DocumentSession` owns the live editor resources, native tab/control,
  file watcher, pending operations, cancellation, and disposal. Its
  `DocumentTabViewModel` reads that session's current values. It contains no
  independently editable copy of document state. Both travel with the session
  when a tab moves to another window.
- **View:** `SettingsPage` receives its existing view model from the window.
  `DocumentTabPresentation` binds a native tab to its display projection.
  Focus, title-bar geometry, drag/drop, XAML roots, and window handles stay in
  view code or Windows services.

`MainWindow` partials under `Views/` group native view wiring by feature. They
remain one class; moving a method to a partial does not create a new ownership
boundary. `MainWindow.Commands.cs` routes native input to existing workflows;
`MainWindow.Documents.cs` connects document events and workspace persistence to
the window. Saving, conflict handling, WebView lifetime, and closing continue
to live in their dedicated controllers.

## Binding conventions

- Use notifying view-model properties with explicit `OneWay` display bindings
  and `TwoWay` editable bindings.
- Keep settings view models long-lived. `LoadPreferences` refreshes display
  state without emitting a preference-write event, so synchronizing windows
  cannot cause a write loop.
- Update session presentation through `DocumentTabViewModel.Refresh()` after
  changing runtime state. Existing window/controller update callbacks provide
  these notification points. Bindings use the session's projection as their
  source, so moving a tab cannot leave its label bound to the old window.
- Change bound state on the owning window's UI thread. `ObservableObject`
  raises notifications on its caller's thread; it does not hide dispatching.
- Preserve controller host interfaces. Controllers still use Windows types
  where required and must not be moved into Core just because they are outside
  a view.

## Project boundaries

Core has folders for `Protocol`, `Documents`, `Recovery`, `Workspace`,
`Library`, `Localization`, `IO`, and `Threading`. Its existing
`ExcalidrawDesktop.Core` namespace is deliberately stable; the folder changes
do not change the shared API or serialized formats. Core has no WinUI or
WebView2 dependency.

The browser entry point only imports styles and mounts `DesktopApp`. Feature
hooks own their subscriptions and cleanup; document/save coordination stays
together in `DesktopApp`. Browser APIs come from the mounted node's
`ownerDocument` and `defaultView`. Smoke APIs remain gated by `desktopSmoke=1`.

Native smoke helpers remain guarded by `DEBUG`. Keeping them under `Testing/`
does not change which code is included in Release builds. Standalone Core and
native transaction tests remain separate, and web tests stay beside their
implementation.

## Validation

Use the commands in the main README for TypeScript, web tests, Core/native
tests, localization, and Debug/Release builds. For presentation or ownership
changes, run the title-bar, multi-window, save/close, recovery, and suspension
smoke suites against the newly published Debug build. The title-bar suite also
checks settings bindings in both directions, suppresses write-back during
settings loading, and checks persistence/restart notices.
The multi-window suite checks separate window view models, shared preference
synchronization without write-back loops, and presentation identity after a
session transfer. See [refactor validation](validation/CODE_ORGANIZATION_VALIDATION.md)
for the completed checks.
