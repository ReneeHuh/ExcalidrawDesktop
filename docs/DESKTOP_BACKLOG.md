# Excalidraw Desktop backlog

Status: Active
Last consolidated: September 2, 2026

This is the canonical checklist for work that remains on Excalidraw Desktop.
The detailed designs remain in:

- [`TABBED_DESKTOP_PLAN.md`](TABBED_DESKTOP_PLAN.md)
- [`WINUI_DESKTOP_PLAN.md`](WINUI_DESKTOP_PLAN.md)
- [`EDGE_STYLE_TITLEBAR_TABS_PLAN.md`](EDGE_STYLE_TITLEBAR_TABS_PLAN.md)
- [`FUTURE_DESKTOP_ROADMAP.md`](FUTURE_DESKTOP_ROADMAP.md)

When an item is completed, update this checklist and the relevant detailed
plan in the same change.

## Repository preparation before initial commit

- [x] Replace the copied upstream Excalidraw source tree with an exact-version
  `@excalidraw/excalidraw` npm dependency.
- [x] Keep web-asset build and Excalidraw-update tooling in the desktop
  repository.
- [ ] Create a desktop-owned Git repository and remote without using the
  upstream Excalidraw repository as its history or origin.
- [x] Move the implementation into the root-level project layout documented in
  `WINUI_DESKTOP_PLAN.md`; do not add a wrapping `src/` or `app/` directory.
- [x] Move the JavaScript package manifest and lockfile into
  `ExcalidrawDesktop.Web` and remove the root Yarn workspace.
- [x] Update solution, project, tool, test, and documentation paths after the
  move, then verify a clean restore, full build, and complete test run.
- [x] Add the desktop project license and direct Excalidraw third-party notice before the
  initial commit.

## Immediate verification

- [ ] Confirm Visual Studio F5 upgrades the installed development package from
  `0.1.3.0` to source version `0.1.4.0` without DEP0700.
- [ ] Record the title-bar manual validation results under `docs/validation`.

## Tabbed desktop MVP completion

### Performance and tab lifetime

- [x] Design safe inactive-tab suspension and full hibernation boundaries.
- [x] Suspend inactive clean tabs after a five-minute idle interval.
- [x] Resume suspended tabs immediately when selected.
- [x] Keep dirty, loading, saving, recovering, or conflicted tabs live until
  their state can be preserved safely.
- [x] Show sleeping and resuming states in the tab and status bar.
- [ ] Add packaged suspension failure-recovery coverage. Basic suspend/resume
  and origin-preservation automation is implemented.
- [x] Rerun and evaluate the 1/5/10/20-tab performance matrix after suspension.
- [x] Fully unload safe clean editors after fifteen inactive minutes and
  recreate them on selection, preserving the native session and isolated origin.
- [ ] Test large scenes and embedded images across multiple tabs.
- [ ] Establish supported tab-count, startup, switching, and memory budgets on
  the minimum supported machine.

The initial Debug x64 baseline is stored in
[`validation/MULTI_TAB_PERFORMANCE.json`](validation/MULTI_TAB_PERFORMANCE.json).
Twenty live tabs reached approximately 2.35 GB working set and 1.73 GB private
memory, which justifies investigating inactive-tab hibernation.
The matching suspension run is stored in
[`validation/MULTI_TAB_PERFORMANCE_SUSPENDED.json`](validation/MULTI_TAB_PERFORMANCE_SUSPENDED.json).
Suspending 18 of 20 tabs reduced working set to approximately 2.26 GB and
private memory to 1.60 GB. Suspension remains useful for background CPU and
timer control, but full clean-tab unloading is required for substantial memory
savings.
The matching unload run is stored in
[`validation/MULTI_TAB_PERFORMANCE_UNLOADED.json`](validation/MULTI_TAB_PERFORMANCE_UNLOADED.json).
Unloading 19 of 20 tabs reduced the measured process-tree working set from
2.35 GB to 588 MB (74.9%) and private memory from 1.73 GB to 298 MB (82.7%).

### Document safety and packaged evidence

- [ ] Prove with packaged tests that Save and Save As affect only the owning
  tab and file.
- [ ] Prove that bridge messages and native file handles cannot cross session
  boundaries.
- [ ] Validate dirty indicators and Save, Don't Save, and Cancel for individual
  tab closes.
- [ ] Validate Save All, Discard All, Review Tabs, and Cancel for window close.
- [ ] Generate real edits, force termination during the recovery debounce
  interval, and verify exact recovery.
- [ ] Add packaged tests for files modified, renamed, moved, and deleted by an
  external process.
- [ ] Test independent IndexedDB, undo history, viewport, zoom, and selection
  state across tabs.
- [ ] Exercise taskbar jump-list selection in packaged UI automation or record
  a manual validation.
- [ ] Round-trip representative drawings and embedded images through
  excalidraw.com without meaningful data loss.

### Title bar, accessibility, and input

- [ ] Validate window drag, restore-from-drag, double-click maximize, and the
  system menu using the dedicated title-bar drag region.
- [ ] Validate minimize, maximize/restore, close, and Windows 11 Snap Layouts.
- [ ] Validate Recent, add-tab, tab selection, close buttons, reordering, and
  middle-click close near the non-client region.
- [ ] Test 1, 5, 10, and 20 tabs at narrow, normal, maximized, and snapped
  window sizes.
- [ ] Test 100%, 125%, 150%, 175%, and 200% scaling, including mixed-DPI
  monitor transitions.
- [ ] Test light, dark, high-contrast, active/inactive, and increased text
  scaling states.
- [ ] Test keyboard-only navigation and shortcuts with focus in both the native
  shell and WebView2.
- [ ] Validate Narrator names, selected and dirty state, and standard caption
  button automation behavior.
- [ ] Validate touch, pen, IME, RTL, and reduced-motion behavior.

### Reliability and user experience

- [x] Add a native Settings page using Windows Community Toolkit SettingsCard
  controls for theme, startup restoration, and tab-lifetime preferences.
- [ ] Add retry and close actions for a tab whose editor fails to initialize.
- [ ] Add clear WebView2 Runtime missing, damaged, and repair guidance.
- [ ] Improve loading, empty-workspace, recovery, conflict, sleeping-tab, and
  failure visuals.
- [ ] Add structured startup, bridge, file, recovery, and WebView diagnostics
  that never include drawing contents or embedded images.
- [ ] Limit bridge and document payload sizes.
- [ ] Explicitly detach WebView event handlers and remove virtual-host mappings
  when tabs close.
- [ ] Verify file watchers, WebViews, and recovery resources are released when
  tabs close.

## Windows desktop and release completion

- [ ] Restore window size, position, maximized state, tab order, and active tab
  safely across launches.
- [ ] Register the `.excalidrawlib` file association.
- [ ] Add pinned drawings to the native Recent menu and taskbar jump list.
- [ ] Add New Drawing and pinned-drawing taskbar jump-list actions.
- [ ] Decide whether image and PDF export remain editor-owned or use a native
  desktop export workflow.
- [ ] Define Release x64 performance and reliability budgets.
- [ ] Produce and validate a signed MSIX package.
- [ ] Choose Microsoft Store distribution, direct MSIX/App Installer
  distribution, or both.
- [ ] Add an update channel and a tested rollback procedure.
- [ ] Add CI for TypeScript checks, web tests, .NET tests, packaged builds, and
  smoke tests.
- [ ] Generate third-party notices and verify preservation of upstream
  licensing.
- [ ] Add ARM64 packaging after x64 behavior is stable.
- [ ] Complete a stable-release installation, update, repair, and removal test.

## Optional post-release product roadmap

### Power-user workflow

- [ ] Add a native command palette for tabs, files, export, view, and app
  commands.
- [ ] Add configurable shortcuts with documented native/editor precedence.
- [ ] Add tab search and quick-open for recent and open drawings.
- [ ] Add duplicate tab, reopen closed tab, and reopen previous workspace.
- [ ] Add optional split view and saved workspace layouts.
- [ ] Expand the native Settings page with focused file, recovery, input,
  privacy, and update controls as those systems are implemented.
- [ ] Add native notifications only for actionable background results or
  failures.

### Templates, libraries, and export

- [ ] Add curated starter templates and personal templates.
- [ ] Add safe template previews and import/exportable template packs.
- [ ] Add a native `.excalidrawlib` manager with pinned libraries and recent
  components.
- [ ] Add a consistent PNG, SVG, and PDF export center.
- [ ] Add export preferences, clipboard export, batch export, cancellation,
  and progress.

### Presentation

- [ ] Treat ordered frames as slides without breaking Excalidraw compatibility.
- [ ] Add a presentation navigator and reorderable slide list.
- [ ] Add full-screen presentation, presenter notes, presenter view, and
  annotate mode.
- [ ] Export presentations as PDF or numbered images.
- [ ] Support links between frames for interactive walkthroughs.

### Local visual knowledge workspace

- [ ] Index approved folders, filenames, frame names, and drawing text locally.
- [ ] Add local search, tags, favorites, collections, thumbnails, and recent
  activity.
- [ ] Add local drawing links, backlinks, and broken-link status.
- [ ] Add a home screen for recent, pinned, tagged, and recoverable drawings.
- [ ] Allow users to exclude folders, pause indexing, and clear derived data.

### Optional intelligence and extensions

- [ ] Define an explicit opt-in provider and privacy model before adding AI.
- [ ] Add previewable, undoable diagram generation, Mermaid conversion, layout
  cleanup, summaries, action extraction, or accessible descriptions.
- [ ] Define a narrow permission model for commands, importers, exporters,
  templates, and scene transformations before supporting extensions.

### Optional sync and collaboration

- [ ] Decide account, hosting, encryption, key, retention, deletion, and
  self-hosting policies before implementation.
- [ ] Design safe local/remote/offline reconciliation.
- [ ] Add opt-in sync, version history, shared boards, presence, comments, and
  conflict visualization only after the data model is proven.
- [ ] Ensure connected failures cannot corrupt or block local drawings.

## Recommended implementation order

1. Inactive-tab suspension and before/after performance evidence.
2. Packaged document-safety, close, recovery, and external-conflict tests.
3. Title-bar, accessibility, input, DPI, and theme validation.
4. Failure experience, diagnostics, and lifecycle hardening.
5. File compatibility, release budgets, signing, CI, and distribution.
6. Optional post-release features in roadmap order.
