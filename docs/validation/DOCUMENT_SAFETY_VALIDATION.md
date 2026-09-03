# Document safety validation

Date: September 3, 2026  
Configuration: Debug x64, packaged WinUI 3 application

## Automated results

The complete packaged smoke suite passed with each script launched in a fresh
PowerShell process. The document-safety additions produced these results:

| Check | Result |
| --- | --- |
| Save two distinct editor scenes to their owning files | Passed |
| Keep native file handles and bridge dispatchers session-bound | Passed |
| Detect an external file modification | Passed |
| Detect an external move or rename | Passed |
| Detect an external deletion | Passed |
| Choose Reload for an interactive external-change conflict | Passed |
| Choose Save As without overwriting the external version | Passed |
| Choose Keep Editing and preserve both dirty editor and disk version | Passed |
| Cancel an individual dirty-tab close without losing the tab | Passed |
| Discard and close an individual dirty tab | Passed |
| Save an individual dirty tab to its owning file and close it | Passed |
| Cancel a window close and preserve all dirty drawings | Passed |
| Review window-close drawings and select the first dirty tab | Passed |
| Save All writes three dirty drawings to their owning files | Passed |
| Generate a real editor edit and recover it after forced termination | Passed |
| Isolate IndexedDB, scene, viewport, zoom, and selection across two tabs | Passed |
| Undo only the active tab's captured editor change | Passed |
| Preserve a 501-element drawing and embedded PNG through unload/recreation | Passed |
| Reject an unsafe unload while retaining the live editor | Passed |
| Preserve the large drawing through three unload/recreate cycles | Passed |
| Isolate Save, Save As, and external changes across two windows | Passed |
| Preserve recovery identity/content through a live window transfer | Passed |
| Reject transfers during initialization, suspend, resume, and unload | Passed |
| Reject a process-failed transfer and recover the isolated editor | Passed |

The recovery test now requests a real Excalidraw scene update through the
session-bound bridge. It then waits for the normal dirty notification and
recovery debounce, terminates the process, relaunches it, and has the restored
editor validate the snapshot.

The state-isolation test assigns distinct real Excalidraw scene, viewport,
zoom, selection, and IndexedDB values to two exact-origin WebViews. It then
issues Ctrl+Z through Chromium's trusted input path and verifies that only the
active tab's undo history changes.

## Broader packaged regression results

Startup, tab/origin and local-storage isolation, workspace restore, title-bar
and accessibility automation, suspension/unload/recreation, file activation,
multi-window transfer, clean and dirty multi-window exit, whole-drawing PNG
export, and German LTR and Arabic RTL localization all passed. The title-bar
run used 125% scaling and also covered light/dark themes, keyboard shortcuts,
automation names, native status, caption buttons, editor retry, and closed-tab
resource cleanup.

## Remaining manual release checks

This environment cannot certify physical touch or pen input, Narrator's spoken
output, IME behavior, high contrast and increased text scaling, or moving the
window between monitors at every target DPI. Those remain open in
`DESKTOP_BACKLOG.md`, along with human translation review and visual review of
PNG output on representative user drawings.
