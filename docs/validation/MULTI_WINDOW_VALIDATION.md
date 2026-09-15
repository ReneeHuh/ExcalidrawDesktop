# Multi-window validation

Last automated run: September 15, 2026

## Automated evidence

- `Invoke-MultiWindowSmokeTest.ps1` (Debug, isolated data root): passed live
  WebView transfer, repeated moves, dirty-state and origin preservation,
  watcher/handler rebinding, unsafe-state rejection, and empty destination
  cleanup. The tab drag checks added with the move from native tear-out to
  `TabView` drag-and-drop also passed: selecting a tab never changes the
  window, tab widths stay equal and shrink together, dropping a live tab
  outside the strip opens a window sized like the source and placed so the
  pointer rests on its tab strip, unavailable tabs create no window, and
  not-yet-initialized and sleeping tabs can start a reorder drag while still
  being refused a cross-window move.
- Core tests: 181 passed, including `TabStripDropIndexTests` (insertion index
  for left-to-right, mirrored right-to-left, scrolled, and unequal-width
  strips) and `WindowPlacementTests` (work-area clamping and dropped-tab
  window placement, including maximized sources).
- Desktop Debug build and self-contained publish: zero warnings and zero
  errors.

Earlier evidence from September 2, 2026 (native tear-out implementation):
`Invoke-MultiWindowExitSmokeTest.ps1` clean and `-Dirty`,
`Invoke-WorkspaceRestoreSmokeTest.ps1`, `Invoke-RecoverySmokeTest.ps1`, and
`Invoke-TitleBarTabsSmokeTest.ps1` passed. Those paths were not changed by the
drag-and-drop work and were not rerun.

## Harness notes

- The smoke scripts require PowerShell 7. Windows PowerShell 5.1 fails to parse
  them (UTF-8 without a byte-order mark) and cannot compile the inline C#
  helper (`out var`).
- Smart App Control blocked one freshly built `ExcalidrawDesktop.Core.Tests.dll`
  with error 0x800711C7 until the assembly changed. Rebuilding after a source
  change cleared it.

## Manual validation still required

- Drag a tab with the mouse into a new window and into another window's tab
  strip, including at the far left and far right of the strip and while the
  destination shows a dialog or file picker (the drop must be refused).
- Reorder a sleeping tab and a never-selected restored tab with the mouse.
- Exercise Save, Save As, Save all, Cancel, and Review individually with dirty
  drawings distributed across several windows.
- Test external modify, move, and delete prompts after cross-window moves.
- Test snapped, maximized, narrow, mixed-DPI, touch, pen, keyboard-only, and
  Narrator behavior. A drop from a maximized source should open a window
  covering most of the work area rather than a maximized-size restored window.
- Record the 1/5/10/20-tab memory matrix across multiple windows.
