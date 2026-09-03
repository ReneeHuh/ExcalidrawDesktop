# Multi-window tab workspace plan

Status: Feature implementation complete (M0-M4); M5 release validation remains
Created: September 2, 2026
Originally sequenced after: the document-safety and lifecycle exit criteria in
[`TABBED_DESKTOP_PLAN.md`](TABBED_DESKTOP_PLAN.md)
Related plans:
[`TABBED_DESKTOP_PLAN.md`](TABBED_DESKTOP_PLAN.md) and
[`EDGE_STYLE_TITLEBAR_TABS_PLAN.md`](EDGE_STYLE_TITLEBAR_TABS_PLAN.md)

## Outcome

Allow one Excalidraw Desktop process to host multiple native windows, with an
independent tab strip in each window. Users can create a second window, move a
live drawing tab into it, tear a tab out by dragging, and drag tabs between
existing windows without losing editor state.

The completed experience should support this shape:

```text
Excalidraw Desktop process
├─ Window A
│  ├─ Architecture.excalidraw
│  └─ Meeting notes.excalidraw ●
└─ Window B
   ├─ Untitled
   └─ Release plan.excalidraw
```

Moving a tab moves its existing document session and WebView. It must preserve
the drawing, undo history, selection, viewport, zoom, browser-storage origin,
file ownership, dirty state, recovery identity, and external-file monitoring.

## Product behavior

### Required interactions

- File > New window creates and activates a new window with one untitled tab.
- A drawing-tab context command moves that tab to a new window.
- `TabView` tab tear-out creates a new window during the drag.
- A torn-out tab can be dragged into another Excalidraw Desktop window at the
  indicated tab position.
- Opening a file that is already open in any window activates its existing
  window and tab rather than opening a duplicate.
- Incoming file activations open in the most recently active usable window.
- Save, Save As, Close Tab, Save All, and adjacent-tab shortcuts apply to the
  window that received the command.
- Settings remains a window-local shell tab and cannot be torn out or moved.

### Window and last-tab rules

- Moving the last drawing out of a source window closes the empty source
  window without creating a replacement tab or showing a save prompt.
- Explicitly closing the last drawing tab retains the current behavior and
  creates a new untitled tab.
- Closing one window resolves only dirty drawings owned by that window.
- Exit resolves dirty drawings across all windows and exits only after every
  window can close.
- Closing the final application window exits the process after persistence and
  recovery work completes.

### Restoration rules

- A normal restart restores the windows that existed when the application last
  exited, subject to the existing reopen-saved-tabs preference.
- Tab order and active tab are restored independently for each window.
- Window bounds and maximized state are restored on a visible monitor and
  clamped to its work area.
- The existing version-2 flat workspace is migrated into one window.
- A window closed earlier in the same run is not resurrected on the next
  launch.

## Architecture decision

Use multiple WinUI `Window` instances in one application process and on the
existing UI thread. Do not use one operating-system process per window.

Windows App SDK 2.1.3 already exceeds the version that introduced native
`TabView` tear-out. Enable `CanTearOutTabs` and implement:

- `TabTearOutWindowRequested`
- `TabTearOutRequested`
- `ExternalTornOutTabsDropping`
- `ExternalTornOutTabsDropped`

Enabling native tear-out suppresses the existing drag/drop completion events,
including `TabDragCompleted`. Tab-order synchronization must therefore move to
the new tear-out/reorder flow or to the collection that backs `TabItemsSource`.

Primary platform references:

- [WinUI TabView tab tear-out](https://learn.microsoft.com/windows/apps/develop/ui/controls/tab-view#tab-tear-out)
- [Show multiple windows for a WinUI app](https://learn.microsoft.com/windows/apps/develop/ui/multiple-windows)
- [Windowing overview](https://learn.microsoft.com/windows/apps/develop/ui/windowing-overview)

## Current constraints

`MainWindow` is currently both the visual shell and the application workspace
coordinator. It owns:

- The session list and tab order.
- The file-activation queue.
- Duplicate-file detection.
- Recent files and jump-list updates.
- Workspace serialization and its write gate.
- Recovery storage and pruning.
- Window-close and Save All coordination.

Several objects also capture their creating window:

- `DocumentService` retains a `Window` for file-picker initialization and a
  dialog root for `ContentDialog`.
- `BridgeDispatcher` callbacks close over methods on the creating
  `MainWindow`.
- Tab pointer handlers and context menus close over the creating window.
- File watchers enqueue callbacks through the creating window's dispatcher.

Consequently, moving only a `TabViewItem` would leave the session partially
owned by its old window. Multi-window support requires an explicit session
detach/attach boundary.

## Target ownership model

```text
App
└─ ApplicationWorkspaceCoordinator
   ├─ WindowManager
   │  ├─ WindowWorkspace A
   │  └─ WindowWorkspace B
   ├─ global open-file registry
   ├─ activation router
   ├─ recent-files and settings services
   ├─ workspace persistence coordinator
   └─ recovery coordinator

WindowWorkspace
├─ MainWindow shell
├─ ordered DocumentSession collection
├─ active session
├─ settings-tab state
└─ window-scoped close and dialog coordination

DocumentSession
├─ document/file/recovery state
├─ live DocumentTabContent and WebView2
├─ isolated TabOrigin
└─ replaceable WindowSessionHost attachment
```

### Application workspace coordinator

Create one application-scoped coordinator from `App`. It tracks windows by
`WindowId`, remembers the most recently active window, owns global services,
and performs atomic session moves.

Its responsibilities are:

- Create, activate, look up, and unregister windows.
- Find a session by canonical document path across every window.
- Route launch, file, drag/drop, recent-file, and jump-list requests.
- Maintain one authoritative recent-file list.
- Serialize one aggregate workspace snapshot.
- Determine the complete retained recovery-ID set before pruning snapshots.
- Coordinate application-wide Exit.

### Per-window workspace

Move the current tab collection and active-tab behavior behind a per-window
host/controller. `MainWindow` continues to own its title bar, status surface,
settings page, XAML roots, and window-scoped dialogs.

The per-window API should expose operations rather than its mutable controls:

```csharp
DocumentSession CreateSession(bool select = true);
DocumentSession DetachSessionForMove(DocumentSession session);
void AttachMovedSession(DocumentSession session, int? index = null);
Task<bool> RequestCloseAsync();
void SelectSession(DocumentSession session);
```

### Transferable document session

Define a `WindowSessionHost` or equivalent attachment containing callbacks and
window-specific UI services. Attaching a session to a new window must rebind:

- File-picker owner HWND and dialog `XamlRoot`.
- Bridge commands for new, open, close, and adjacent-tab navigation.
- Tab pointer handlers and context flyout commands.
- Editor host and `TabViewItem` parentage.
- Title, status, selection, and lifecycle notifications.
- External-file-change UI dispatch.

The following identity must not change during a move:

- `DocumentSession` instance.
- `DocumentTabContent` and live `CoreWebView2` when loaded.
- `DesktopTabOrigin` and mapped host.
- `DocumentService` active file and file stamp.
- Recovery ID, dirty state, and pending recovery work.

Do not implement transfer by serializing and reopening the scene. That would
discard transient editor state and create avoidable recovery races.

### Atomic move transaction

A move follows one coordinator-controlled transaction:

```text
validate source, destination, and session
          ↓
mark session as moving; block close/unload/save prompts
          ↓
detach window-bound handlers and remove source visuals
          ↓
attach visuals and handlers to destination
          ↓
select session and refresh both windows
          ↓
close an empty tear-out source if required
          ↓
persist the completed aggregate workspace
```

If attachment fails, restore the session to the source window. Never leave a
session absent from every window or attached to both windows.

## Persistence version 3

Replace the flat workspace layout with an aggregate window layout:

```csharp
WorkspaceStateV3
  Version
  RecentFiles
  LastActiveWindowId
  Windows[]
    Id
    Bounds
    IsMaximized
    ActiveRecoveryId
    Tabs[]
      Path
      WasDirty
      RecoveryId
      DisplayName
      RecoveryUpdatedAt
```

Use a logical persisted window ID rather than persisting the runtime
`AppWindow.Id`. Runtime IDs are used only to route live tear-out events.

Only the application coordinator writes this file. Coalesce rapid selection,
reorder, resize, and move notifications so multiple windows cannot race to
replace the same state file.

Recovery pruning must run after every restorable window has been reconstructed
and the complete set of dirty recovery IDs is known. Closing one window must
not delete snapshots retained by another window.

## File activation and duplicate ownership

Keep the current single-instance process redirection. Replace the single
`App.window` target with this routing order:

1. Normalize each activated path.
2. Search the global session registry for an existing owner.
3. If found, activate its window and select its tab.
4. Otherwise use the most recently active, ready window.
5. Create a window only when no usable window exists.

Global duplicate-file detection must also be used by Open, Recent, drag/drop,
and Save As. The current per-window callback is insufficient once two windows
can own sessions.

## Close, save, and recovery safety

- Window Close takes a stable snapshot of that window's dirty sessions and
  uses the existing Save All, Discard All, Review, or Cancel flow.
- Application Exit prevents new windows and transfers, then closes windows in
  a deterministic order. Cancellation leaves all remaining windows open.
- A session being moved cannot simultaneously close, unload, suspend, open a
  picker, or participate in a second move.
- A window cannot close while it owns an unresolved transfer.
- Recovery writes remain keyed by the unchanged session recovery ID.
- Persistence never treats an in-flight move as a closed tab.

## Delivery phases

### Phase M0: Extract ownership without behavior changes

- [x] Add an application coordinator and explicit per-window ownership
  boundaries, covered by core and packaged workspace tests. `MainWindow`
  remains the concrete per-window host rather than adding a second wrapper
  abstraction solely to match the original proposal.
- [x] Move recent files, persistence, recovery pruning, and path ownership to
  the application coordinator.
- [x] Track the existing main window by runtime and logical window ID.
- [x] Preserve all current single-window behavior and smoke tests.

Exit criteria:

- One-window startup, restoration, open, save, recovery, and close behave as
  before.
- `MainWindow` is no longer the sole owner of application-global state.
- Only one component writes workspace state or prunes recovery snapshots.

### Phase M1: Create and manage independent windows

- [x] Add File > New window and a keyboard-accessible command.
- [x] Track activation order and route new file activations.
- [x] Give each window its own title bar, settings tab, status, and tab host.
- [x] Implement final-window process shutdown.
- [x] Add basic two-window packaged automation.

Exit criteria:

- Two windows can independently create, edit, save, and close tabs.
- Opening an already-open file activates the correct window.
- Closing one window does not dispose sessions or recovery data in another.

### Phase M2: Safe command-driven tab moves

- [x] Implement the atomic detach/attach transaction.
- [x] Add Move tab to new window to the drawing-tab context menu.
- [x] Rebind every window-capturing service and handler.
- [x] Preserve the live WebView, origin, undo stack, viewport, and dirty state.
- [x] Roll back to the source window when destination attachment fails.

Exit criteria:

- Clean, dirty, suspended, unloaded, recovered, and externally watched tabs
  either move safely or are rejected with no state loss.
- Repeated moves do not duplicate handlers, watchers, tabs, or WebViews.
- Moving the only tab closes the empty source window safely.

### Phase M3: Native tear-out and cross-window drag

- [x] Enable `CanTearOutTabs` for drawing tabs.
- [x] Create a destination window in `TabTearOutWindowRequested`.
- [x] Transfer the session in `TabTearOutRequested`.
- [x] Accept and place application-owned tabs through the external tear-out
  events.
- [x] Replace `TabDragCompleted`-dependent ordering logic.
- [x] Reject settings tabs and invalid or concurrent transfers.

Exit criteria:

- A drag can tear a tab into a window, snap that window during the drag, and
  merge the tab into another existing window.
- Cancelled and failed drags leave a valid selected tab in every open window.
- Keyboard and pointer tab reordering still persist correctly.

### Phase M4: Multi-window restore and placement

- [x] Add version-3 workspace models and version-2 migration tests.
- [x] Persist logical windows, tab membership/order, active tabs, and bounds.
- [x] Restore bounds safely across disconnected and mixed-DPI monitors.
- [x] Debounce aggregate workspace writes.
- [x] Restore all dirty sessions before pruning recovery snapshots.

Exit criteria:

- A forced termination and restart restores the same window/tab membership
  and every recoverable dirty drawing.
- Normal exit and restart restore the intended windows without resurrecting a
  window closed earlier in the run.
- Corrupt or off-screen placement data falls back to a visible default.

### Phase M5: Hardening and release evidence

- [x] Test Save and Save As isolation across windows.
- [x] Test window-close and application-exit decisions with several dirty
  drawings.
- [x] Test external modification, move, and deletion across windows.
- [x] Test transfer during initialization, recovery, suspend, resume, unload,
  and WebView process failure. Packaged automation rejects each unsafe state,
  preserves recovery identity/content during a live transfer, and verifies
  process-failure retry recreates the editor with its isolated origin intact.
- [ ] Test narrow, maximized, snapped, and mixed-DPI windows.
- [ ] Test keyboard-only use, Narrator, touch, pen, and title-bar input.
- [ ] Rerun the 1/5/10/20-tab memory matrix across one and several windows.
- [x] Verify all WebViews, file watchers, handlers, and empty tear-out windows
  are released.

Exit criteria:

- No tested action can make two sessions own the same canonical file.
- No move, close, crash, or restore scenario loses dirty drawing content.
- Cross-window behavior has packaged automation plus a recorded manual matrix.

## Expected repository changes

Likely additions:

```text
ExcalidrawDesktop.App/
├─ Models/
│  ├─ WindowWorkspace.cs
│  └─ WindowSessionHost.cs
└─ Services/
   ├─ ApplicationWorkspaceCoordinator.cs
   ├─ WindowManager.cs
   └─ ActivationRouter.cs

ExcalidrawDesktop.Core/
├─ MultiWindowWorkspaceState.cs
└─ SessionMoveRules.cs
```

Likely modifications:

- `App.xaml.cs`: track windows and delegate activation.
- `MainWindow.xaml`: add native tear-out events and New window UI.
- `MainWindow.xaml.cs`: become a per-window host and delegate global work.
- `DocumentSession.cs`: add move state and replaceable host attachment.
- `DocumentService.cs`: use the currently attached window for pickers/dialogs.
- `WorkspaceStateStore.cs`: support version 3 and version 2 migration.
- Core tests and packaged smoke tests: add multi-window and transfer coverage.

Names are provisional. Preserve clear ownership boundaries rather than
creating abstractions solely to match this file layout.

## Principal risks

| Risk | Required mitigation |
| --- | --- |
| Old-window callbacks survive a move | Centralize binding and test handler counts after repeated moves |
| A WebView cannot be safely reparented in a lifecycle state | Gate moves by state and prove each supported state with packaged tests |
| Two windows race to persist | One aggregate writer with coalescing and atomic replacement |
| A window prunes another window's recovery | Compute retained IDs globally after restore/close transactions |
| Save As creates duplicate ownership | Enforce canonical paths in an application-wide registry |
| Empty tear-out windows leak | Track provisional windows and close them on cancel/failure |
| Restored bounds are off-screen | Validate monitor availability and clamp to a work area |
| Exit partially closes the workspace | Deterministic application-level close transaction with cancellation |

## Effort estimate

For one developer familiar with this codebase:

| Slice | Estimate |
| --- | ---: |
| M0 ownership extraction | 3-5 days |
| M1 independent windows | 2-3 days |
| M2 safe session moves | 4-6 days |
| M3 native tear-out | 3-5 days |
| M4 persistence and restoration | 3-5 days |
| M5 automation and hardening | 4-7 days |

A demonstrable two-window prototype is feasible in under one week. The full,
recovery-safe and release-ready feature is approximately two to three focused
developer weeks, depending on what packaged automation reveals about live
WebView reparenting.

## Definition of done

Multi-window tabs are complete when:

- Users can create, close, snap, and restore multiple tabbed windows.
- A live tab can move between windows through both a command and native drag
  tear-out without losing editor state.
- File activations and duplicate-file checks work across the entire process.
- Window Close and application Exit protect every dirty document.
- Workspace and recovery state survive forced termination at each tested move
  boundary.
- No window owns global persistence or globally prunes recovery state.
- The packaged multi-window automation and manual accessibility/input/DPI
  matrix pass on the supported Windows configuration.
