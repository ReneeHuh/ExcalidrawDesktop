# Closing, reopening, and Exit

Implemented in 0.1.7.

## Shortcuts

| Command | Shortcut |
| --- | --- |
| New window | Ctrl+N |
| New drawing tab | Ctrl+T |
| Reopen last closed tab or window | Ctrl+Shift+T |
| Close active tab | Ctrl+W |
| Close active window | Ctrl+Shift+W, Alt+F4, or X |
| Exit all windows | File > Exit |
| Open | Ctrl+O |
| Save / Save As | Ctrl+S / Ctrl+Shift+S |
| Save all drawings in this window | Ctrl+Alt+S |
| Next / previous tab | Ctrl+Tab / Ctrl+Shift+Tab |
| Select tab / last tab | Ctrl+1 through Ctrl+8 / Ctrl+9 |

Native accelerators and editor forwarding use these meanings. Repeated keydown
events do not repeatedly create, close, or reopen drawings. The + button always
creates a blank drawing; Ctrl+T does not change meaning when history exists.

## Behavior

| Situation | Result |
| --- | --- |
| Close a dirty or untitled drawing | Keep a local draft and add a closed-tab entry; no drawing Save/Discard/Cancel prompt |
| Close B while A remains open | Keep A open; record B and its drawing tabs as one history group |
| Reopen B | Recreate its tabs, order, selected drawing, and placement within a visible monitor |
| Close B, then close a tab in A | Reopen that tab first, then B |
| File > Exit from A+B | Checkpoint both before closing either; restore both on the next launch |
| Close B, then Exit A | Restore A at startup; B stays in closed history |
| Close the final window | Restore it at startup without a duplicate history entry |
| Close the final drawing tab | Keep the window with one blank replacement tab |
| Move the final drawing elsewhere | Close the empty source without creating history |
| Close Settings | Hide Settings without adding drawing history |
| No closed history | Reopen does nothing; native history commands are disabled |
| Clean file changed on disk | Reopen its latest disk contents |
| Missing file or invalid draft | Keep the history entry and report failure |
| File already open | Select its existing owner; retain any separate dirty draft in history |

File > Recently closed lets the user choose an older entry. This also allows a
retained draft to be selected after closing an already-open copy of its file.
A partly duplicated window restores the other tabs and retains its separate
dirty drafts. It never creates two writable owners for the same path.

Closing and Exit preserve unsaved edits without writing to the original file.
Save and Save As still write to the selected drawing file. Exit and closing the
last window always restore the complete captured workspace, including blank
tabs, even when saved-tab restoration after an interrupted session is disabled.

The old automatic-save-on-close toggle is replaced by an explanation of draft
preservation. Its persisted field is retained for settings-file compatibility.
Library-save failures retain their separate explicit discard/cancel decision.

## Storage and retention

```text
%LOCALAPPDATA%\\ExcalidrawDesktop\\
  workspace-state.json
  workspace-state.recovery\\
    <recovery-id>.excalidraw
```

The version-4 workspace format stores active windows separately from a
chronological closed-item history. Versions 1–3 migrate on load. Drafts contain
the complete drawing, including embedded images and the saved-file baseline.

History retains the latest 20 clean entries and every entry containing an
unsaved drawing. There is no automatic eviction of the only unsaved copy.
Startup orphan detection and recovery pruning include closed-history references.
Installer upgrades preserve this data directory.

## Commit and failure rules

1. Freeze the editor and let the current edit settle.
2. Receive its final scene and atomically write the recovery draft.
3. Check the acknowledged document generation and commit workspace/history.
4. Only then dispose the tab or window.

Exit prepares every window before committing the complete startup workspace.
A dialog, export, failed editor, checkpoint failure, or changed document
generation prevents closing. All prepared windows unlock if preparation fails.
Sleeping or unloaded editors are resumed/recreated before acknowledgement;
a failed editor is never assumed clean.

Reopening reads and validates sources, stages disabled tabs, waits for successful
editor loads, and commits the active workspace together with history consumption.
Failed restoration removes only the provisional tabs and retains history.
Presentation failures after commit cannot roll back the recovered drawings.

ApplicationWorkspaceCoordinator owns cross-window history and Exit transitions.
WindowCloseController and DocumentLifecycleController retain close/save and
document responsibilities. DocumentSession owns live editor state.

## Verification

- Core tests cover close/Exit transitions, grouped history, retention, migration,
  persisted drafts, and workspaces exceeding 100 draft tabs.
- Mounted web tests cover frozen checkpoint content, exact shortcut modifiers,
  text-input dispatch, and repeated key events.
- `tools/Test-CloseReopen.ps1` exercises real WebViews and a separate restart:
  immediate dirty close, draft/metadata write failures, library cancellation,
  saved-file recovery and explicit Save, duplicate/missing files, tab/window
  shortcuts, grouped window restoration, aborted multi-window Exit, pruning,
  and full startup restoration with the saved-tab preference disabled.
- Rapid initialization during the native scenario also covers the gap between
  Navigate completing and the page becoming ready. Repeated initialization
  reuses that navigation rather than aborting the page load.
