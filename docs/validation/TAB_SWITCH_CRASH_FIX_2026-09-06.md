# Tab-switch crash: native tear-out window contract

## Reproduction and cause

The reported runs ended after `tab.selected` and `tab.tear_out_started` with
`targetReady:false`. The handler returned early when `CanMoveSession` was false,
leaving `TabViewTabTearOutWindowRequestedEventArgs.NewWindowId` unset.

WinUI raises that event for ordinary tab clicks as well as drags. Its
[OnEnteringMoveSize implementation](https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/TabView/TabView.cpp)
uses the supplied window ID unconditionally. The callback does not expose a
cancellation property.

A real mouse click on an editor held in initialization reproduced the failure
before the fix. Windows Application Error event 1000 at 2026-09-06 16:51:36 UTC
reported `Microsoft.UI.Input.dll`, exception `0xe0464645`, offset `0x39ee6`.
The diagnostic log ended at the same two events as the user's log; the managed
exception hooks did not record this native failure.

## Change

- Always provide an unregistered temporary destination, including for unavailable
  editors, non-document tabs, and clicks while an exit prompt is open.
- Keep the existing transfer eligibility checks on actual moves.
- On rejection, post `WM_CANCELMODE` and defer empty-window disposal until after
  the native move loop exits. WinUI must still be able to use the destination
  when our transfer callback returns. Failed-transfer rollback also leaves the
  native destination alive until this cleanup.
- Preserve transition suppression and exclusion from the taskbar during
  preparation. Register and reveal the window only after a successful transfer.
- Log preparation as `tab.tear_out_window_requested` from `native`; reserve
  started/completed/rejected for actual tear-outs and record completion after
  success. Add the assembly informational version to startup records.

## Validation

- Debug and Release self-contained publish: passed.
- Real mouse click on the same held-initialization tab after the fix: selected
  the loading editor and remained responsive with one workspace window.
- Real mouse tear-out while a missing-file dialog blocked transfer: rejected
  without crashing; source retained both tabs and the temporary window closed.
- Release, clean isolated workspace: a real mouse drag moved a ready tab into a
  separate live window; the log recorded successful tear-out completion.
- `Invoke-MultiWindowSmokeTest.ps1 -SkipBuild -TimeoutSeconds 90`: passed on the
  final build, including new checks for failed/initializing/non-document tabs,
  valid native destinations, deferred rejection cleanup, and the existing
  editor identity, save, recovery, suspension, and repeated-transfer checks.
- `Invoke-MultiWindowExitSmokeTest.ps1 -SkipBuild -TimeoutSeconds 60`: passed;
  both windows exited and the aggregate workspace was persisted.
- An earlier run timed out recreating an unloaded editor. A fresh isolated run
  and the final automated run passed; no production timeout was changed.
- All test-owned processes were stopped and the interaction request removed.

The smoke helper can pause for native mouse input by placing
`tear-out-interaction-smoke.request` beside the Debug executable before starting
the multi-window smoke scenario. It holds one editor initializing until the
file is removed (maximum ten minutes), then resumes the automated checks.
The checkpoint must be used with an isolated smoke data root.

Published executables:

- `ExcalidrawDesktop.App/bin/x64/Debug/unpacked/ExcalidrawDesktop.exe`
- `ExcalidrawDesktop.App/bin/x64/Release/unpacked/ExcalidrawDesktop.exe`
