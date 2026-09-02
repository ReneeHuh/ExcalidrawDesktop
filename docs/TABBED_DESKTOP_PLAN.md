# Excalidraw Desktop Tabbed Workspace Plan

Status: Core implementation complete; document-safety, compatibility,
performance, and manual Windows validation remain
Created: September 1, 2026
Related plan: [`WINUI_DESKTOP_PLAN.md`](WINUI_DESKTOP_PLAN.md)
Related implemented plan: [`MULTI_WINDOW_TABS_PLAN.md`](MULTI_WINDOW_TABS_PLAN.md)

## Current delivery snapshot

| Area | Implementation | Verification |
| --- | --- | --- |
| Native tabs and per-tab WebView2 | Implemented | Packaged two-origin smoke coverage; broader interaction checks remain manual |
| Unique exact per-tab origins | Implemented | Unit-tested exact matching and packaged `localStorage` isolation |
| Per-tab open, save, Save As, dirty state, and close protection | Implemented | Unit/web coverage is partial; packaged cross-tab save-isolation coverage remains |
| Reorder, adjacent navigation, and close/close-others/close-right commands | Implemented | Core ordering helpers are unit tested |
| Middle-click close, Copy Path, and Reveal in Explorer | Implemented | Packaged and manual interaction coverage remains |
| Recent files and clean workspace restoration | Implemented | Packaged restore smoke coverage |
| Per-tab recovery after forced termination | Implemented | Synthetic packaged snapshot/restore coverage; real-edit debounce validation remains |
| External modification, move, and deletion protection | Implemented | File-stamp logic is unit tested; packaged conflict-flow coverage remains |
| Consolidated window close | Save All, Discard All, Review Individually, or Cancel implemented | Per-document selection in one dialog is not implemented |
| File association and existing-instance file activation | Implemented | Packaged cold-start and existing-instance automation passes |
| Multi-file drag/drop and jump lists | Implemented | Jump-list selection still needs recorded validation |
| Tabs in the native title bar | Implemented | Hardware-dependent manual matrix remains; see [`EDGE_STYLE_TITLEBAR_TABS_PLAN.md`](EDGE_STYLE_TITLEBAR_TABS_PLAN.md) |
| Multiple windows and tab tear-out | Implemented | M5 hardening remains; see [`MULTI_WINDOW_TABS_PLAN.md`](MULTI_WINDOW_TABS_PLAN.md) |
| Performance target and hibernation decision | Measured and implemented | Five-minute suspension and fifteen-minute safe clean-editor unloading |

“Implemented” describes code present in the repository. It does not imply that
every manual matrix item or every originally proposed internal abstraction has
been completed.

## Outcome

Build Excalidraw Desktop as a desktop-first, tabbed drawing workspace. Each tab represents one independent Excalidraw document with its own editor state, browser-storage origin, file identity, dirty state, undo history, and recovery state.

The native WinUI shell owns tabs, files, application lifecycle, recovery, and Windows integration. Excalidraw continues to own the canvas, scene model, rendering, selection, and undo/redo behavior.

## Product goals

1. Make working with several drawings feel as natural as using tabs in a browser or code editor.
2. Prevent data or browser-storage state from leaking between tabs.
3. Treat normal `.excalidraw` files as the authoritative user documents.
4. Preserve unsaved work across accidental closes, crashes, and application restarts.
5. Keep the canvas familiar and place desktop workspace features in the native shell.
6. Maintain a narrow integration layer so the exact-version Excalidraw npm package can be updated independently.

## Historical non-goals for the first tabbed milestone

- Cloud synchronization or real-time collaboration.
- Multiple application windows. *(Implemented later under the dedicated
  multi-window plan.)*
- A native rewrite of the Excalidraw canvas.
- Unlimited simultaneously active WebView2 controls.
- Treating `localStorage` or IndexedDB as the authoritative saved document.

These features may be considered after the local tabbed document experience is reliable.

## User experience

The main window contains a native WinUI `TabView` above the Excalidraw surface.

```text
┌──────────────────────────────────────────────────────────────┐
│ Board A ●  × │ Architecture × │ Meeting notes × │  +       │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│                    Active Excalidraw canvas                   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

The dot marks unsaved changes. The active tab controls the window title, current file commands, export target, and close behavior.

Required interactions:

- `Ctrl+T`: create a new tab.
- `Ctrl+O`: open a drawing in a new tab.
- `Ctrl+S`: save the active tab.
- `Ctrl+Shift+S`: save the active tab as a new file.
- `Ctrl+W`: close the active tab.
- `Ctrl+Tab` and `Ctrl+Shift+Tab`: move between tabs.
- Middle-click a tab to close it. *(Remaining.)*
- Drag tabs to reorder them.
- Use a tab context menu to close, close others, close tabs to the right, copy
  the path, or reveal the file in Explorer. *(Close commands are implemented;
  Copy Path and Reveal remain.)*
- Drop one or more `.excalidraw` files on the window to open them as tabs.
  *(Remaining.)*
- Opening a file that is already open focuses its existing tab.
- Closing a dirty tab offers Save, Don't Save, and Cancel.
- Closing the window resolves all dirty tabs without silently losing work.

## Architecture decision

### One document session and one WebView2 per live tab

Each live tab owns a `DocumentSession` and a WebView2 editor host. Keeping the editor instance alive preserves its selection, viewport, undo history, text editing state, and dialogs when the user switches tabs.

```text
MainWindow (current workspace coordinator)
├─ ordered DocumentSession list
├─ workspace/recovery stores
├─ recent files and close coordination
└─ tab creation, activation, persistence, and WebView lifecycle
   ├─ DocumentSession A
   │  ├─ Document identity and file state
   │  ├─ WebViewHost
   │  └─ https://tab-{session-a}.excalidraw.local/
   ├─ DocumentSession B
   │  ├─ Document identity and file state
   │  ├─ WebViewHost
   │  └─ https://tab-{session-b}.excalidraw.local/
   └─ DocumentSession C
      ├─ Document identity and file state
      ├─ WebViewHost
      └─ https://tab-{session-c}.excalidraw.local/
```

The originally proposed `DocumentWorkspace` abstraction has not been extracted.
`MainWindow` currently performs that role. This is accepted for the current
functional milestone, but the large code-behind should be split before adding
multiple windows, tab tear-out, or substantially more workspace policy.

Inactive clean WebViews suspend after five minutes and fully unload after
fifteen minutes. The native session, file ownership, watcher, recovery identity,
and unique origin remain alive; selecting the tab recreates only its WebView.

### Per-tab origin isolation

WebView2 controls that use the same URL share origin-scoped `localStorage`, IndexedDB, cache storage, and service-worker state. Separate WebViews alone do not isolate this data.

Every runtime tab therefore receives a unique private HTTPS hostname:

```text
https://tab-8f3c...excalidraw.local/index.html
https://tab-a421...excalidraw.local/index.html
```

The hostname uses a randomly generated runtime session identifier safe for DNS labels, such as `tab-{Guid:N}.excalidraw.local`. Each host maps to the same packaged `ExcalidrawDesktop.App/Assets/Web` directory using WebView2 virtual-host mapping. The web bundle is not copied per tab.

Rules:

- A tab's hostname is unique for that live session.
- A reopened file does not depend on receiving its previous hostname.
- Native code validates navigation, permissions, and bridge messages against the exact origin assigned to that session.
- A wildcard `*.excalidraw.local` check is not sufficient for privileged bridge access.
- Top-level navigation away from the assigned origin is blocked; approved external links open in the system browser.
- The production bundle does not register a service worker.

Per-tab origins provide isolation and defense in depth. They do not make browser storage the document database.

### Native document authority

WinUI remains authoritative for:

- Runtime document and session identifiers.
- Active and pending `StorageFile` references.
- Canonical file paths and normalized path comparisons.
- Display names and untitled numbering.
- Native dirty-state presentation and close/save coordination. The web editor
  owns scene-version comparison and reports whether the current scene is dirty.
- Native Open, Save, and Save As dialogs.
- Atomic writes.
- Recovery snapshots.
- Recent files and session restoration.
- Duplicate-file detection.

The web editor owns:

- Scene elements, app state, and embedded files.
- Serialization and restoration through Excalidraw APIs.
- Selection, viewport, editing gestures, and undo/redo history.
- Per-editor transient state.

The native host loads file contents into the assigned editor and receives serialized content for save or recovery. Browser storage may contain unavoidable or transient editor state, but it must never be the only copy of unsaved work.

### Shared preferences

Theme, language, spell-check, and other application-wide preferences should not
be inferred by copying one tab's browser storage. A native shared-settings
service has not yet been implemented. Until it is, do not add cross-tab storage
copying as a shortcut; define native ownership and send settings to each editor
when it becomes ready.

Document-specific view state, such as zoom and scroll position, may remain inside the live editor and later be included in native session metadata if restoration requires it.

## Native models and services

### `DocumentSession`

The current model is a session-bound container used directly by `MainWindow`.
It owns:

```csharp
DocumentSession
  TabOrigin
  DisplayName
  RecoveryId / RecoveryUpdatedAt / RecoveryGate
  ExternalFileState / ExternalFileWatcher
  IsDirty
  IsReady
  IsInitializing
  close/save coordination state
  DocumentService
  DocumentTabContent / TabViewItem / CoreWebView2
  BridgeDispatcher
  PendingDocumentLoad
```

`TabOrigin` contains the randomly generated runtime identity. It is not derived
from the file path and is not persisted as file content. File identity remains
inside the per-session `DocumentService`.

The proposed `IsSaving`, `IsFaulted`, and native `LastSavedSceneVersion`
properties are not present. Save-version race protection currently lives in the
web editor, which compares the serialized scene version with the current scene
before reporting clean state. If save coordination grows more complex, promote
an explicit saving/version state into the native session rather than creating a
second unsynchronized source of truth.

### Workspace coordinator

`MainWindow` currently owns:

- The ordered session list and its corresponding `TabView.TabItems` order.
- The active session.
- New, open, activate, reorder, and close operations.
- Duplicate-file detection.
- Window-close coordination across dirty sessions.
- Restoring the previous tab order and active tab.

### Current and target service split

The current native structure is:

```text
ExcalidrawDesktop.App/
├─ Models/
│  └─ DocumentSession.cs
├─ Controls/
│  └─ DocumentTabContent.xaml(.cs)
├─ Services/
│  ├─ DocumentService.cs
│  └─ BridgeDispatcher.cs
└─ MainWindow.xaml(.cs)

ExcalidrawDesktop.Core/
├─ WorkspaceStateStore.cs
├─ RecoverySnapshotStore.cs
└─ workspace/path/origin/protocol helpers
```

Each tab owns a separate mutable `DocumentService`, so file state is not shared
between tabs. The proposed `DocumentWorkspace`, `DocumentFileService`,
`RecoveryService`, `RecentFilesService`, `SessionStateService`, and
`WebViewHostFactory` have not been extracted. Treat those names as a future
refactoring direction, not as current architecture. Extraction should be driven
by testability or the needs of multi-window/tab-tear-out work.

## WebView and bridge lifecycle

1. `MainWindow.CreateTab()` creates a `DocumentSession`, editor host, and unique origin.
2. `MainWindow.InitializeSessionAsync()` initializes WebView2 and maps the session hostname to the packaged web assets.
3. Navigation and permission handlers close over that session's exact origin.
4. The editor sends `app.ready`.
5. Native sends pending initial content for that session. Shared native preferences remain future work.
6. Editor changes update only the owning session's dirty and recovery state.
7. Save requests serialize only the owning editor and write only its `StorageFile`.
8. Closing a tab removes the session UI, explicitly detaches file-watcher,
   routed-input, and CoreWebView2 handlers, clears its virtual-host mapping,
   closes the WebView, and deletes recovery data when safe.

The native host derives the owning session from the WebView/dispatcher association. It must not trust a web-provided document identifier to select an arbitrary native session. If `documentId` is added for diagnostics, it must match the session already bound to that WebView.

## File and tab behavior

### New

- Create a new untitled session without replacing the active tab.
- Assign display names `Untitled`, `Untitled 2`, and so on.
- Do not mark a blank tab dirty until the scene meaningfully changes.

### Open

- The current Open picker selects one `.excalidraw` file. File activation can
  enqueue multiple paths. Multi-select picker/drop support remains T4 work.
- Normalize paths and focus an existing session when the file is already open.
- Validate before creating a visible usable editor tab.
- Show an error for the affected tab without disturbing other tabs.

### Save and Save As

- Save operates only on the active session.
- Save As updates that session's file identity and recent-file entry.
- If Save As selects a path already open in another tab, ask the user to choose another path or focus the existing document.
- A successful save clears dirty state only if the editor has not changed since serialization began.
- Use atomic writes and retain the recovery snapshot until save is confirmed.

### Close tab

- Clean tabs close immediately.
- Dirty tabs show Save, Don't Save, and Cancel.
- A failed or cancelled save leaves the tab open.
- If the final tab closes while the window remains open, create one new untitled tab.

### Close window

- Present a consolidated list when several tabs are dirty.
- The current dialog offers Save All, Discard All, Review Tabs Individually, and
  Cancel. This is the accepted first implementation.
- Per-document selection inside the consolidated dialog is optional follow-up
  work, not a completed capability.

## Recovery and session restoration

Recovery is native and per session:

- Debounce scene snapshots for two seconds after meaningful changes.
- Use separate recovery files keyed by session ID.
- Store metadata separately from drawing content: original path, display name, tab order, active tab, and snapshot timestamp.
- Never overwrite an explicit `.excalidraw` file during recovery.
- Remove a recovery snapshot only after a confirmed save or explicit discard.
- On unclean startup, show recoverable drawings before deleting anything.

Normal session restoration remembers clean file-backed tabs and reopens them
from disk. Unsaved content is restored from recovery snapshots. The current
policy skips clean missing files and prunes them from recent files; it does not
create an error tab. A recovered dirty tab can survive without its original
file and later be saved or located. If product requirements change to preserve
clean missing tabs as error tabs, specify and test that separately.

## Performance strategy

Reliability takes priority over early optimization.

Initial policy:

- Create a WebView lazily when a tab first becomes active.
- Keep initialized inactive WebViews alive and hidden.
- Dispose WebViews after their tab is safely closed.
- Share the WebView2 environment while relying on unique origins for storage isolation.
- Measure private memory, switch latency, and startup time with 1, 5, 10, and 20 tabs.

If measurements require hibernation:

1. Serialize the inactive editor into a native recovery snapshot.
2. Record enough view metadata to restore the experience.
3. Dispose its WebView after a defined inactivity or memory threshold.
4. Recreate the WebView and restore the snapshot when activated.
5. Document and test any unavoidable loss of undo history.

Do not implement hibernation until the live-tab design is correct and measured.

## Delivery phases

### Phase T0: Refactor without changing behavior

Status: Functionally implemented. WebView content and per-tab file state are
session-bound; WebView initialization remains in `MainWindow` rather than a
factory service.

Tasks:

- Extract the WebView visual host into `DocumentTabContent`. *(Implemented.)*
- Keep initialization and security event wiring in `MainWindow` for now; move it
  to a factory only when lifecycle hardening or multi-window work requires it.
- Replace singleton mutable file state with a `DocumentSession` for the current document.
- Make `BridgeDispatcher` session-bound.
- Keep one visible tab and preserve existing New, Open, Save, Save As, and close behavior.

Exit criteria:

- Existing Phase 0 and Phase 1 tests still pass.
- The app still launches, edits, opens, saves, and closes one document.
- No document-specific mutable state remains global in `MainWindow` or a singleton file service.

### Phase T1: Two isolated untitled tabs

Status: Functionally implemented with an architectural deviation:
`MainWindow` is the workspace coordinator; no separate `DocumentWorkspace`
class exists.

Tasks:

- Add native `TabView` and workspace coordination. *(Implemented in
  `MainWindow`; extraction remains optional refactoring.)*
- Create and switch between untitled tabs.
- Assign and enforce a unique origin for every session.
- Keep each WebView alive while switching.
- Add tab-level dirty indicators.

Exit criteria:

- Two tabs can contain different drawings without scene replacement.
- Writing the same `localStorage` key in both tabs produces different values.
- Undo, selection, zoom, and scroll remain independent.
- A bridge message from one origin cannot operate on another session.

### Phase T2: Complete per-tab file lifecycle

Status: Functionally implemented September 1, 2026. Packaged cross-tab save,
Save As, cancellation, and close-prompt automation remains to be added.

Tasks:

- Open files into new tabs.
- Save and Save As the active tab.
- Detect files already open.
- Implement single-tab close prompts.
- Route document keyboard shortcuts to the active session.
- Update titles and tooltips with file and dirty state.

Exit criteria:

- Saving one tab never writes another tab's file.
- Opening an already-open file focuses its tab.
- Cancelled pickers and prompts do not disturb any tab.
- Closing a dirty tab cannot silently discard work.

### Phase T3: Workspace lifecycle

Status: Substantially implemented September 2, 2026, including external
modification, move, and deletion protection. Interaction and verification gaps
remain.

Tasks:

- [x] Reorder tabs and implement Close, Close Others, and Close Right commands.
- [x] Add middle-click close and Copy Path.
- [x] Add consolidated Save All, Discard All, Review, and Cancel window-close handling.
- [x] Add recent files and restore the previous clean session.
- [x] Add per-tab crash recovery.
- [x] Handle missing or externally changed files under the current skip/recover policy.
- [x] Show per-tab path, save, recovery, and conflict state in a bottom status bar.
- [ ] Add packaged external-conflict and real-edit recovery/debounce coverage.

Exit criteria:

- Forced termination can recover every dirty tab within the agreed snapshot interval.
- Tab order and active tab restore correctly.
- External file conflicts are never overwritten without a user decision.

### Phase T4: Windows desktop integration

Status: Feature implementation complete — `.excalidraw` registration,
single-instance routing, multi-file activation and drag/drop, duplicate-tab
focusing, native Recent-menu activation, jump lists, and title-bar integration
are implemented. Manual accessibility and input validation remains.

The title-bar portion of this phase is specified in
[`EDGE_STYLE_TITLEBAR_TABS_PLAN.md`](EDGE_STYLE_TITLEBAR_TABS_PLAN.md).

Tasks:

- [x] Open file activations as tabs in the running application.
- [x] Add packaged automation for cold-start and existing-instance activation.
- [x] Support multi-file drag and drop.
- [x] Register `.excalidraw` file associations.
- [x] Activate files from the native Recent menu.
- [x] Add Copy Path and Reveal in Explorer.
- [x] Add jump lists.
- [ ] Complete the manual accessibility and input validation for the implemented
  native title-bar tab strip.

Exit criteria:

- Explorer activation focuses an existing tab or opens a new one predictably.
- External links never replace a tab's editor content.
- Keyboard, touch, pen, high-DPI, and accessibility checks pass with the tab strip present.

### Phase T5: Measurement and polish

Status: In progress — a packaged 1/5/10/20-tab startup, switching, and memory
measurement harness was added September 2, 2026.

The first Debug x64 packaged baseline is recorded in
[`validation/MULTI_TAB_PERFORMANCE.json`](validation/MULTI_TAB_PERFORMANCE.json).
All 20 tabs became ready in 10.4 seconds, shell switching remained below 15 ms,
and the full WinUI/WebView2 process tree reached 2.35 GB working set and 1.73 GB
private memory. Responsiveness is acceptable on the measured machine, but the
roughly linear WebView2 memory growth justifies a separate inactive-tab
hibernation design before treating 20 continuously live editors as the default
support target.

The matching suspended baseline is recorded in
[`validation/MULTI_TAB_PERFORMANCE_SUSPENDED.json`](validation/MULTI_TAB_PERFORMANCE_SUSPENDED.json).
Suspending 18 of 20 tabs reduced working set by about 3.5% and private memory
by about 7.1%. Keep suspension for inactive CPU and timer control, but proceed
to full unloading of safe clean tabs for meaningful memory reduction.

The matching full-unload baseline is recorded in
[`validation/MULTI_TAB_PERFORMANCE_UNLOADED.json`](validation/MULTI_TAB_PERFORMANCE_UNLOADED.json).
Unloading 19 of 20 tabs reduced process-tree working set by about 74.9% and
private memory by about 82.7%. File-backed content is size-checked, validated,
and cached before disposal; dirty, busy, recovering, or conflicted tabs never
enter this path. Recreating a clean editor preserves the tab origin but resets
its in-memory undo history.

Tasks:

- Test large scenes and embedded images across multiple tabs.
- Record Release-build memory and switching performance on the minimum
  supported machine.
- Validate hibernation with large scenes, embedded images, external changes,
  and repeated unload/recreate cycles.
- Improve loading, failure, recovery, and empty-workspace visuals.
- Add structured diagnostics that never contain drawing contents.

Exit criteria:

- The agreed tab-count target remains responsive on the minimum supported machine.
- A failed editor tab can be retried or closed without crashing the workspace.
- Packaged smoke tests cover multi-tab startup, isolation, save, restore, and shutdown.

## Testing strategy

The lists below are the target coverage. Current automated evidence consists of:

- C# unit tests for paths, launch arguments, drop filtering, origins, file
  stamps, bridge parsing, document validation, workspace metadata, recovery
  storage, and ordered-tab helpers.
- Web tests for bridge correlation and document open/save/restore behavior.
- Packaged smoke tests for startup, two-origin `localStorage` isolation, clean
  workspace restoration, synthetic two-tab recovery after forced termination,
  and Explorer activation into a single running instance.

Current automation does **not** yet prove IndexedDB isolation, independent undo
and viewport state, cross-tab Save/Save As ownership, dirty-close decisions,
real-edit recovery debounce, external-conflict dialogs, or taskbar jump-list
selection. Keep those items open until a packaged test or a recorded manual
validation provides evidence.

### Unit tests

- Workspace collection and active-session transitions.
- Untitled name allocation.
- Canonical path comparison and duplicate-file detection.
- Dirty-state changes during overlapping edit and save operations.
- Close-decision state machine.
- Session persistence and recovery metadata.
- Exact-origin validation.

### Web tests

- Initial content is applied to the intended editor.
- Serialization is requested from the intended editor.
- Scene version changes produce correct dirty events.
- Shared settings do not replace document content.
- The desktop entry point does not register a service worker or rely on browser scene persistence.

### Integration tests

- Two virtual origins receive isolated `localStorage` and IndexedDB state.
- Messages are routed by the WebView-bound session.
- Cross-origin and mismatched-session bridge messages are rejected.
- Switching tabs preserves independent scene and view state.
- Save and Save As update only the owning session.

### Packaged black-box tests

1. Launch with one blank tab.
2. Draw distinct shapes in two tabs and switch repeatedly.
3. Save both tabs to different files and verify their contents.
4. Attempt to open the same file twice and verify focus behavior.
5. Close dirty tabs through Save, Don't Save, and Cancel paths.
6. Force termination with multiple dirty tabs and verify recovery.
7. Restart normally and verify session order and active tab.
8. Open one or more files from Explorer while the app is already running.

## Security requirements

- Generate origin hostnames in native code; never accept them from web content.
- Bind each dispatcher to one `DocumentSession` and exact expected origin.
- Do not expose arbitrary filesystem paths through bridge messages.
- Continue using native pickers and validated `StorageFile` references.
- Limit document and bridge payload sizes.
- Dispose controls when tabs close, explicitly detach WebView event handlers,
  and clear the per-tab virtual-host mapping. Packaged title-bar automation
  verifies this cleanup for a closed tab.
- Never log scene contents, embedded images, or recovery data.

## Major risks and mitigations

| Risk | Mitigation |
| --- | --- |
| WebViews share browser storage | Give every session a unique HTTPS origin and test storage isolation in the packaged app. |
| A message operates on the wrong document | Bind dispatchers to WebViews and sessions; never select a session solely from a web-provided ID. |
| Multiple WebViews consume excessive memory | Initialize lazily, measure realistic tab counts, and add safe snapshot-based hibernation only if required. |
| Save clears dirty state after a newer edit | Associate serialization with a scene version and clear dirty state only when that version is still current. |
| Closing several dirty tabs creates confusing prompts | The current consolidated dialog offers Save All, Discard All, Review Individually, and Cancel; evaluate per-document selection only if usability testing requires it. |
| Browser persistence conflicts with native recovery | Keep native files and recovery authoritative; disable upstream scene persistence in the desktop entry point. |
| Upstream Excalidraw changes assume one global document | Keep tab ownership in the desktop integration layer and cover isolation with integration tests. |

## Definition of done for the tabbed desktop MVP

- [x] Users can create, open, reorder, switch, save, and close multiple drawing tabs.
- [x] Every live tab has a unique exact origin and packaged `localStorage` isolation evidence.
- [ ] Packaged evidence proves file operations and bridge messages cannot cross session boundaries.
- [ ] Dirty indicators and Save/Don't Save/Cancel close paths have recorded end-to-end validation.
- [x] Duplicate file opens focus the existing tab.
- [x] Synthetic multiple-dirty-tab recovery passes after forced termination.
- [ ] Real edits recover within the agreed debounce interval after forced termination.
- [x] Normal restart restores the previous clean workspace.
- [x] The packaged x64 application passes the current multi-tab smoke tests offline.
- [ ] The application remains responsive at an agreed supported tab count.
- [ ] Representative `.excalidraw` files round-trip with excalidraw.com without meaningful data loss.

## Next implementation slices

1. Add packaged save-isolation, close-decision, external-conflict, and
   real-edit recovery validation so existing behavior satisfies its exit
   criteria with evidence.
2. Record the remaining title-bar, accessibility, input, DPI, and jump-list
   manual validation.
3. Validate full unloading with realistic large drawings and embedded images,
   then define the supported tab-count and memory budgets.
4. Round-trip representative drawings with excalidraw.com and complete the
   remaining release gates in [`DESKTOP_BACKLOG.md`](DESKTOP_BACKLOG.md).

Do not extract a `DocumentWorkspace` solely to match the original proposal.
Perform that refactor when tests, multi-window support, or measured maintenance
cost justify it.

Multi-window support was implemented before all tabbed-MVP evidence was
closed. Keep the remaining safety gates blocking release, then continue with
the desktop product phases in
[`FUTURE_DESKTOP_ROADMAP.md`](FUTURE_DESKTOP_ROADMAP.md).
