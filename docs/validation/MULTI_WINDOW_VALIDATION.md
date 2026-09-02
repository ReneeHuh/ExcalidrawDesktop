# Multi-window validation

Last automated run: September 2, 2026

## Automated evidence

- `Invoke-MultiWindowSmokeTest.ps1`: passed live WebView transfer, repeated
  moves, dirty-state and origin preservation, watcher/handler rebinding,
  unsafe-state rejection, and empty destination cleanup.
- `Invoke-MultiWindowExitSmokeTest.ps1`: passed coordinated clean Exit across
  two windows, process termination, and aggregate version-3 persistence.
- `Invoke-MultiWindowExitSmokeTest.ps1 -Dirty`: passed two sequential dirty
  window dialogs using Discard all, recovery cleanup, final persistence, and
  process termination.
- `Invoke-WorkspaceRestoreSmokeTest.ps1`: passed version-1 migration to the
  aggregate version-3 format, tab restoration, active-tab restoration, and
  missing-file pruning.
- `Invoke-RecoverySmokeTest.ps1`: passed forced-termination restoration for
  two dirty untitled tabs using version-3 window metadata.
- `Invoke-TitleBarTabsSmokeTest.ps1`: passed title-bar DPI, keyboard, theme,
  accessibility, editor retry, and closed-tab resource cleanup checks.
- Core tests: 45 passed. Web tests: 19 passed. Desktop build: zero warnings and
  zero errors.

## Manual validation still required

- Drag a tab out into a new window and merge it into an existing window.
- Exercise Save, Save As, Save all, Cancel, and Review individually with dirty
  drawings distributed across several windows.
- Test external modify, move, and delete prompts after cross-window moves.
- Test snapped, maximized, narrow, mixed-DPI, touch, pen, keyboard-only, and
  Narrator behavior.
- Record the 1/5/10/20-tab memory matrix across multiple windows.
