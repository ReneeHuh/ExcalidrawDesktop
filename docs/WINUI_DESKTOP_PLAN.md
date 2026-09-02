# Excalidraw Desktop WinUI Implementation and Test Plan

Status: Core desktop implementation complete; release validation and
distribution work remain
Created: August 31, 2026
Repository baseline: `master` at `e1bb9ff8`

## Outcome

Build **Excalidraw Desktop**, a Windows desktop edition of Excalidraw using a native WinUI 3 shell and the existing React editor hosted in WebView2.

The WinUI application owns the window, lifecycle, native file dialogs, file associations, title bar, recovery, Windows integration, and MSIX packaging. The existing Excalidraw React packages continue to own the canvas, editor UI, scene model, rendering, undo/redo, import, and export behavior.

This approach preserves compatibility with upstream Excalidraw and avoids rewriting roughly 181,000 lines of TypeScript, TSX, and styles in XAML and Win2D.

## Product principles

1. Preserve the `.excalidraw` format exactly enough to round-trip existing drawings without data loss.
2. Work completely offline for local drawing and file operations.
3. Feel like a Windows application, not a remote website in a window.
4. Keep changes to the upstream editor isolated so future Excalidraw updates remain practical.
5. Make local editing reliable before adding collaboration and cloud features.
6. Treat pen, touch, keyboard, accessibility, high-DPI, and multiple monitors as core requirements.

## Architecture decision

### Selected approach

Use:

- C# and WinUI 3 for the desktop host.
- The stable Windows App SDK available when implementation begins.
- WebView2 for the Excalidraw React editor.
- Vite to produce a desktop-specific web bundle.
- A versioned, typed message bridge between C# and TypeScript.
- Single-project MSIX for the first supported distribution.

### Product identity

- User-facing product and application display name: `Excalidraw Desktop`.
- Solution, project, assembly, and package identifier prefix: `ExcalidrawDesktop`.
- Repository root: the desktop-first repository, with upstream editor code supplied by the exact-version `@excalidraw/excalidraw` npm dependency.
- Documentation may use "desktop app" descriptively, but product UI, packaging metadata, test names, and release artifacts must use `Excalidraw Desktop` consistently.

### Why not a complete native rewrite

A native XAML/Win2D editor would need to reproduce Excalidraw's element model, rough rendering, binding, selection, snapping, text editing, font handling, import/export, image processing, libraries, history, clipboard, touch, pen, accessibility, and collaboration behavior. It would also require continuously porting upstream changes.

A native rewrite can be reconsidered only if a hard requirement prohibits WebView2. It is not part of this plan.

## Relevant existing integration points

- The published editor component exposes `initialData`, `onChange`, `onExport`, and an imperative API through `@excalidraw/excalidraw`.
- The desktop integration deliberately consumes public package exports rather than importing upstream source modules.
- Browser-backed document commands are suppressed by the desktop-specific React entry point so native document ownership remains authoritative.

## Target repository layout

The desktop repository uses root-level project folders. A wrapping `src/` or
`app/` directory is intentionally not used. Each application or test project
has an explicit `ExcalidrawDesktop.*` name, while build orchestration and
documentation remain in lower-case support folders.

```text
ExcalidrawDesktop/
├─ ExcalidrawDesktop.App/            WinUI 3 application and MSIX package
│  ├─ App.xaml
│  ├─ MainWindow.xaml
│  ├─ Controls/
│  ├─ Models/
│  ├─ Pages/
│  ├─ Services/
│  ├─ Assets/Web/                    Generated desktop web bundle
│  ├─ Package.appxmanifest
│  └─ ExcalidrawDesktop.App.csproj
├─ ExcalidrawDesktop.Core/           Platform-independent desktop logic
│  ├─ Bridge/
│  ├─ Documents/
│  ├─ Recovery/
│  ├─ Workspace/
│  └─ ExcalidrawDesktop.Core.csproj
├─ ExcalidrawDesktop.Web/            Desktop React entry point and bridge
│  ├─ src/
│  ├─ package.json
│  ├─ yarn.lock
│  ├─ tsconfig.json
│  ├─ vite.config.mts
│  └─ vitest.config.mts
├─ ExcalidrawDesktop.Core.Tests/     C# unit tests
├─ SmokeTests/                       Packaged PowerShell tests
├─ tools/                            Build, update, and deployment scripts
├─ docs/                             Plans and validation evidence
├─ artifacts/                        Generated packages and reports; ignored
├─ Directory.Build.props
├─ global.json
└─ ExcalidrawDesktop.sln
```

The uncommitted implementation was moved into this layout before the initial
desktop repository commit:

```text
app/native   -> ExcalidrawDesktop.App
app/bridge   -> ExcalidrawDesktop.Core
app/web      -> ExcalidrawDesktop.Web
tests/bridge -> ExcalidrawDesktop.Core.Tests
tests/*.ps1  -> SmokeTests
```

`ExcalidrawDesktop.Web` owns its `package.json`, `yarn.lock`, and
`node_modules`; the repository root is not a JavaScript monorepo. Generated
web assets remain below `ExcalidrawDesktop.App/Assets/Web` for MSIX packaging
but are not committed. Project-owned scripts under `tools/` restore npm
dependencies, build and copy the bundle, validate Excalidraw updates, and
build the WinUI application.

## Runtime design

### Local web content

The WinUI host will map the packaged web assets to a private HTTPS origin such as:

```text
https://app.excalidraw.local/index.html
```

WebView2 virtual host mapping will be used instead of a `file://` URL. This preserves origin-based browser features such as IndexedDB and local storage, supports secure-context APIs, and allows relative asset and chunk loading.

Release builds will:

- Permit top-level navigation only to the private application origin.
- Open approved external links through the Windows shell or default browser.
- Reject bridge messages from unexpected origins.
- Disable WebView2 developer tools and browser UI unless a diagnostic flag is enabled.
- Avoid loading the editor itself from a remote server.

### Native bridge

Control messages use a versioned JSON envelope that distinguishes requests, responses, and events, and carries a structured error shape:

```ts
type BridgeMessage = {
  version: 1;
  kind: "request" | "response" | "event";
  requestId: string; // correlates a response with its request; unique per event
  method: string;
  payload?: unknown;
  error?: { code: string; message: string }; // responses only
};
```

Initial bridge operations:

- `app.ready`
- `document.new`
- `document.open`
- `document.opened`
- `document.save`
- `document.saveAs`
- `document.changed`
- `document.saved`
- `document.export`
- `window.setTitle`
- `window.confirmClose`
- `shell.openExternal`
- `settings.get`
- `settings.set`

Small control messages travel through WebView2 web messaging. Large drawings and image exports must avoid repeated base64 copies; after the compatibility spike, use shared buffers, streams, or a chunked transfer protocol where measurements show it is necessary.

### Document ownership

The native host owns:

- Current file path.
- Display filename.
- Dirty state.
- Open and Save dialogs.
- File association activation.
- Close confirmation.
- Atomic writes.
- Recovery snapshots and cleanup.
- Recent files.

The web editor owns:

- Scene elements and application state.
- Binary image data used by the scene.
- Scene restoration and validation.
- Undo/redo history.
- JSON, PNG, and SVG generation.
- Library state exposed to the host.

The `.excalidraw` file is authoritative once a user explicitly opens or saves a document. Browser storage may cache UI preferences only. The desktop entry point disables web-side scene persistence entirely; native recovery snapshots are the sole recovery source. There must never be two recovery mechanisms that can disagree about the current document.

### Suppressing the editor's built-in file UI

The editor package ships its own Open, Save, and Export menu items and handles Ctrl+O/Ctrl+S internally through browser file APIs. In WebView2 those pickers work, which would create a second save path that bypasses native path tracking, dirty state, atomic writes, and recent files.

The desktop entry point must therefore:

- Set `UIOptions.canvasActions.loadScene: false` and `saveToActiveFile: false`.
- Decide explicitly whether `saveAsImage` and `export` stay web-side or route through the bridge.
- Re-provide Open, Save, and Save As as custom main-menu entries backed by the bridge.
- Intercept document shortcuts (Ctrl+N, Ctrl+O, Ctrl+S, Ctrl+Shift+S) so they reach the native document service — either the shell captures accelerators before WebView2 sees them, or the web side captures and forwards them over the bridge. This decision is part of Phase 1, not Phase 2, because the document lifecycle depends on it.

Dirty state is tracked with the editor's scene versioning (`getSceneVersion`), not raw `onChange` invocations, because `onChange` fires on many non-mutating events and would over-trigger unsaved-changes prompts.

### Window model

The original single-document window assumption was superseded first by
[`TABBED_DESKTOP_PLAN.md`](TABBED_DESKTOP_PLAN.md) and then by
[`MULTI_WINDOW_TABS_PLAN.md`](MULTI_WINDOW_TABS_PLAN.md). The implemented model
is one application process with multiple native windows, each containing a
native tab strip. Every drawing tab owns a transferable document session,
WebView2 editor, unique private origin, file identity, dirty state, and
recovery state.

## Delivery phases

### Phase 0: Compatibility spike

Estimated effort: 2–3 development days.

Implementation status: complete. The packaged editor starts offline, uses a
private virtual HTTPS origin, and has automated startup and bridge coverage.
Hardware-dependent input and compatibility cases remain part of the release
validation matrix.

Tasks:

- Create a minimal packaged WinUI 3 application.
- Add a full-window WebView2 control.
- Create a desktop Vite build from the existing editor.
- Copy the generated bundle into the WinUI package during the build.
- Load it from a private virtual HTTPS origin.
- Add a minimal ready/ping bridge.
- Establish a repeatable command that builds both halves.

Validation matrix:

- Mouse drawing and selection.
- Touch pan, zoom, and drawing.
- Pen input and pressure behavior.
- Keyboard shortcuts and focus.
- Text editing, IME input, and font loading.
- Copy and paste of elements, text, images, PNG, and SVG. Note: clipboard read in WebView2 requires handling `PermissionRequested` in the host; wire this up before concluding a paste failure is an editor incompatibility.
- Image insertion and export.
- Dynamic locales and lazy-loaded chunks.
- Dark and light modes.
- High-DPI rendering.
- Complete offline startup.

Exit criteria:

- A packaged application starts offline and renders the editor.
- A representative drawing can be created, copied, exported, and reopened.
- No blocking incompatibility is found in WebView2.
- The combined build is documented and reproducible.

### Phase 1: Desktop document lifecycle

Estimated effort: approximately 1.5–2 weeks after Phase 0 (includes suppressing the editor's built-in file UI and the shortcut interception strategy).

Implementation status: the native New, Open, Save, Save As, and dirty-close vertical slices are implemented with typed bridge contracts, active-file ownership, 50 MB validation, transactional writes, native menu commands, Ctrl+N/Ctrl+O/Ctrl+S/Ctrl+Shift+S interception, and Save/Discard/Cancel close protection. The manual embedded-image and cross-app round-trip criteria remain before Phase 1 exit.

Tasks:

- Create a desktop-specific React entry point using `@excalidraw/excalidraw`.
- Remove PWA installation and service-worker behavior from the desktop entry point.
- Exclude web-only analytics, promotions, and tab synchronization.
- Disable web-side scene persistence so browser storage never competes with the native document (see "Suppressing the editor's built-in file UI").
- Disable the editor's built-in Open and Save actions via `UIOptions.canvasActions` and re-provide them through custom main-menu entries backed by the bridge.
- Decide and implement the interception strategy for Ctrl+N, Ctrl+O, Ctrl+S, and Ctrl+Shift+S (native accelerator capture or web-side forwarding).
- Implement New, Open, Save, and Save As through the native bridge.
- Use the existing Excalidraw serializer and restore logic.
- Track filename and dirty state using scene versioning (`getSceneVersion`).
- Update the native window title.
- Prompt before destructive New, Open, or Close operations.
- Write files atomically through a Windows transacted stream or temporary file followed by replacement.

Exit criteria:

- Existing `.excalidraw` files open correctly, including embedded images.
- Files saved by the desktop app reopen correctly on excalidraw.com.
- Save updates the active file without showing another picker.
- Save As selects and remembers a new path.
- Canceling any picker or prompt leaves the document unchanged.
- Closing a dirty document cannot silently discard work.
- The editor's built-in browser file pickers are unreachable; every open and save path goes through the native document service.
- Document shortcuts (Ctrl+N/O/S, Ctrl+Shift+S) reach the native document service, not the editor's internal handlers.

### Phase 2: Native Windows integration

Estimated effort: approximately 1 week.

Implementation status: substantially complete. `.excalidraw` association,
single-instance activation, multi-file drag/drop, native title-bar tabs,
recent files, jump lists, theme integration, and external navigation handling
are implemented. `.excalidrawlib`, pinned-item actions, and remaining manual
Windows validation are still open.

Tasks:

- Register `.excalidraw` and `.excalidrawlib` associations in MSIX.
- Handle Explorer double-click and file activation.
- Define `.excalidrawlib` activation behavior: opening a library file merges it into the user's library after confirmation; it is not a document open.
- Support drag-and-drop opening.
- Add native keyboard command routing for remaining non-document shortcuts where WebView focus would otherwise interfere (document shortcuts are handled in Phase 1).
- Add a native title bar with filename and dirty indicator.
- Integrate system theme and preserve the editor's explicit theme preference.
- Open external URLs in the default browser.
- Store and display recent files.
- Add app icons and file-type icons.

Exit criteria:

- Double-clicking an associated file opens it.
- A second file activation is handled predictably while the app is running.
- Standard shortcuts such as Ctrl+N, Ctrl+O, Ctrl+S, and Ctrl+Shift+S work consistently.
- External links never replace the application content in WebView2.

### Phase 3: Reliability and polish

Estimated effort: 1–3 weeks.

Implementation status: substantially complete. Recovery, external-file
monitoring, multi-window placement restoration, clean-tab suspension and
unloading, editor retry UI, WebView2 repair guidance, and lifecycle cleanup are
implemented. Large-document validation, structured diagnostics, payload
limits, and the remaining manual accessibility/input/DPI matrix are open.

Tasks:

- Add debounced native recovery snapshots.
- Detect an unclean shutdown and offer recovery at the next launch.
- Remove obsolete recovery files after confirmed saves.
- Restore window size, position, and state safely across monitor changes.
- Validate mixed-DPI and multiple-monitor movement.
- Test large scenes and large embedded images.
- Optimize bridge transfers based on measurements.
- Add user-facing WebView2 runtime failure handling.
- Complete keyboard, screen-reader, contrast, and reduced-motion checks.
- Add structured diagnostic logging without recording drawing content.

Exit criteria:

- Forced termination loses no more than the defined autosave interval.
- Recovery never overwrites an explicit user file without confirmation.
- Large representative documents do not freeze the UI during save or open.
- Moving between displays does not blur or incorrectly scale the canvas.

### Phase 4: Optional collaboration and cloud features

This phase begins only after the local editor is stable.

Decisions required:

- Use existing Excalidraw collaboration services, support self-hosted endpoints, or ship local-only.
- Decide whether Excalidraw+ links and promotions belong in the desktop product.
- Define authentication behavior inside or outside WebView2.
- Define telemetry and crash-reporting policy.

Potential tasks:

- Bring across Socket.IO and Firebase collaboration support.
- Handle collaboration links through a registered application protocol.
- Add connectivity and offline state indicators.
- Confirm end-to-end encryption compatibility with the web application.

### Phase 5: Packaging and release

Implementation status: in progress. A packaged Debug x64 build and smoke suite
exist; signing, CI, distribution/update decisions, release budgets, and stable
install/update/repair/removal validation remain.

Tasks:

- Produce signed x64 MSIX packages.
- Add ARM64 after x64 behavior is stable.
- Decide between Store distribution, direct MSIX/App Installer distribution, or both.
- Implement the app-update mechanism for the chosen channel: Store auto-update, or a hosted `.appinstaller` feed for direct distribution. Direct distribution makes this a real work item, not a checkbox.
- Add CI for web tests, TypeScript checks, .NET tests, packaged builds, and smoke tests.
- Generate third-party notices and preserve the Excalidraw MIT license.
- Document WebView2 Runtime requirements and recovery behavior, and provide
  Retry, Close tab, and Microsoft repair-help actions in the editor failure UI.
- Establish an upstream update and regression-testing process.

## Testing strategy

### Format compatibility

Maintain fixtures covering:

- Empty and simple scenes.
- Every element type.
- Bound arrows and labeled arrows.
- Frames, groups, links, and locked elements.
- Images and cropped images.
- Custom and legacy fonts.
- Libraries.
- Large drawings.
- Files created by multiple upstream Excalidraw versions.

Each fixture must pass desktop-to-web and web-to-desktop round trips without losing meaningful scene data.

### Automated layers

- Existing Excalidraw unit and type tests remain the primary editor regression suite.
- TypeScript tests cover bridge validation, serialization coordination, and desktop state transitions.
- .NET tests cover message dispatch, atomic file writing, recovery, recent files, and activation routing.
- Integration tests exercise bridge request/response behavior.
- Packaged smoke tests cover startup, open, save, close, and file activation.

### Manual Windows matrix

- Windows 11 current release.
- The agreed Windows 10 baseline (decide before Phase 0 — see "Default decisions pending confirmation" — because it sets the Windows App SDK floor and the WebView2 runtime test scope).
- x64 and later ARM64.
- Mouse, precision touchpad, touch display, and pen.
- 100%, 150%, 200%, and mixed display scaling.
- Light, dark, high-contrast, and reduced-motion settings.
- Standard keyboard layouts plus representative IME and RTL languages.
- WebView2 present, missing, and update-failure scenarios.

## Security requirements

- Only packaged, trusted application content may call privileged bridge methods.
- Validate the source origin, message version, method, request identifier, payload shape, path, and size.
- Do not expose a generic native file-read or command-execution bridge.
- Do not accept arbitrary output paths from web content; paths come from native activation or a native picker.
- Use allowlists for external URI schemes and reject local-file or executable schemes.
- Apply maximum document and transfer sizes with clear errors.
- Keep WebView2 profile data in an application-owned location.
- Disable remote debugging and developer tools in production builds.
- Never log scene contents, collaboration keys, or embedded image data.

## Major risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Clipboard, pen, IME, or drag-and-drop differs in WebView2 | Test in Phase 0 before building the full shell; add narrow native fallbacks only where required. |
| Browser storage conflicts with native files | Disable web-side scene persistence in the desktop entry point; native snapshots are the sole recovery source and the native document service is authoritative. |
| Editor's built-in pickers create a second save path | Disable `loadScene`/`saveToActiveFile` via `UIOptions.canvasActions`, replace with bridge-backed menu entries, and intercept document shortcuts in Phase 1. |
| Large file transfers cause memory spikes | Measure real scenes; use streaming, shared buffers, or chunking instead of repeated base64 encoding. |
| Vite chunks, locales, fonts, or WASM fail offline | Bundle all required assets and test production builds through the virtual HTTPS origin. |
| External content gains native bridge access | Origin-check every message, restrict navigation, and expose only narrow validated operations. |
| Upstream updates become difficult | Use only public package APIs, pin the npm version, and validate every update through the project-owned update tool. |
| Collaboration expands the first release indefinitely | Keep cloud and collaboration explicitly outside the local MVP. |
| Packaging works only on the development machine | Add a clean-machine packaged smoke test in CI before beta. |

## Definition of the local desktop MVP

The MVP is complete when all of the following are true:

- The application is a packaged WinUI 3 desktop application.
- The editor starts and works without an internet connection.
- Mouse, keyboard, touch, and pen pass the compatibility matrix.
- New, Open, Save, Save As, and Close use a reliable native document lifecycle.
- `.excalidraw` files round-trip with the web application without meaningful data loss.
- Embedded images remain intact.
- Explorer file activation works.
- Dirty state and unsaved-changes prompts work.
- Recovery is available after an unclean shutdown.
- External navigation is contained and the bridge is origin-validated.
- An x64 MSIX can be produced from a documented clean build.

Collaboration, cloud storage, multiple windows, Store publication, and ARM64 are not required for the local MVP.

## Effort outline

For one experienced developer:

| Milestone                                   |            Expected effort |
| ------------------------------------------- | -------------------------: |
| Compatibility spike                         |                   2–3 days |
| Local document MVP                          |            3–4 weeks total |
| Polished beta                               |            6–9 weeks total |
| Collaboration and broader release hardening | Scope after the local beta |

These are engineering estimates, not fixed delivery commitments. Signing, Store enrollment, branding, backend work, and product feedback can extend the schedule.

## Confirmed decisions and remaining release decisions

Confirmed:

- Product name: `Excalidraw Desktop`.
- Technology: C# WinUI 3, WebView2, and the React editor.
- Packaging model: MSIX.
- Architecture: one process with multiple tabbed native windows.
- Product scope: local and offline editing, with collaboration deferred.
- Repository structure: root-level `ExcalidrawDesktop.App`, `.Core`, `.Web`, and
  `.Core.Tests` project folders; no wrapping `src/` or `app/` directory.
- Upstream strategy: consume an exact published
  `@excalidraw/excalidraw` version without storing upstream source here.

Still to decide or complete:

- The supported Windows baseline and minimum-machine performance budgets.
- Store, direct MSIX/App Installer distribution, or both.
- The update channel and rollback procedure.
- Whether image and PDF export stay editor-owned or move into a native export
  workflow.

## Current implementation sequence

The original Phase 0 slice is complete. The canonical remaining order is now
maintained in [`DESKTOP_BACKLOG.md`](DESKTOP_BACKLOG.md), beginning with
document-safety evidence and the remaining Windows validation matrix.
