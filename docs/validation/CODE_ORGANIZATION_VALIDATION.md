# Code organization refactor validation

Validated September 14–15, 2026 on Windows x64 using the locally published
Debug application. Desktop smoke scripts ran sequentially with PowerShell
7.6.6 and their isolated smoke data directory.

## Scope

- Adopted ReadPlease-inspired feature folders, long-lived window/settings
  view models, and a per-session document presentation model.
- Grouped native services and Core source files by responsibility, keeping
  Core's public namespace and project boundary stable.
- Split native window wiring into feature partials and browser startup into
  a small entry point, `DesktopApp`, and library/export/smoke hooks.
- Kept application-wide services in the workspace coordinator and live
  editor resources in `DocumentSession`.
- Compared moved Core and service implementations against the original
  sources; their behavior is unchanged apart from imports and namespaces.

## Automated checks

| Check | Result |
| --- | --- |
| Core tests, Release | 121 passed |
| Native file-transaction tests, Release | 6 passed |
| Web tests | 86 passed across 9 files |
| TypeScript typecheck | Passed |
| Frozen Yarn install | Passed |
| Production web bundle | Passed |
| Localization resource validation | Passed |
| Native Debug and Release builds | Passed, zero warnings/errors |
| Debug self-contained publish | Passed |
| `git diff --check` | Passed |

The web suite includes cancellation of an unfinished image export when the
editor unmounts, including a late renderer completion. Node type declarations
are pinned to `22.20.2` so the existing test sources typecheck reproducibly.

## Native integration checks

All suites below used `-SkipBuild -TimeoutSeconds 90` against the published
Debug application.

| Script under `SmokeTests/` | Result and coverage |
| --- | --- |
| `Invoke-TitleBarTabsSmokeTest.ps1` | Passed: native tab layout/accessibility, themes, dynamic status bindings, settings bindings, persistence notices |
| `Invoke-MultiWindowSmokeTest.ps1` | Passed: transfers, editor/session identity, save and recovery isolation, separate window view models, shared settings synchronization |
| `Invoke-MultiWindowExitSmokeTest.ps1` | Passed in clean and `-Dirty` modes |
| `Invoke-DocumentSafetySmokeTest.ps1` | Passed: save/bridge isolation and external-file conflict decisions |
| `Invoke-CloseDecisionsSmokeTest.ps1` | Passed: cancel, discard, save, review tabs, and save all |
| `Invoke-RecoverySmokeTest.ps1` | Passed: forced termination and restoration of two dirty untitled drawings |
| `Invoke-WorkspaceRestoreSmokeTest.ps1` | Passed: restored tabs/selection and missing recent-file pruning |
| `Invoke-ImageExportSmokeTest.ps1` | Passed: full-scene PNG with embedded image, binary transfer, and transactional write |
| `Invoke-TabSuspensionSmokeTest.ps1` | Passed: suspend/resume, unload/recreate, failed-unload recovery, and three repeated recreation cycles |
| `Invoke-LocalizationSmokeTest.ps1` | Passed for German and Arabic, including RTL layout, new editors, and retried editors |

### Unverified file-association check

`Invoke-FileActivationSmokeTest.ps1` failed before its activation assertions:
it could not find a registration for the published executable. Its existing
registration launch uses `Start-DesktopTestApplication`, which sets
`EXCALIDRAW_DESKTOP_SMOKE_TEST=1`. `App.LaunchAsync` deliberately skips
`EnsureFileTypeRegistration` in that mode. The existing registered command
pointed to a different build location.

Those startup and script behaviors predate this refactor. File-association
registration was not changed or verified by this work. A follow-up should
separate registration validation from isolated activation tests.

## Quality review and fixes

A separate subagent performed a thorough source review after implementation,
then reviewed the resulting corrections. No remaining refactor defect was
identified in its final review.

- Removed resource IDs that overwrote new status/restart bindings. Dynamic
  strings continue to use localized view-model properties.
- Set the menu popup items' data context explicitly; they do not inherit the
  main layout's context across the native popup boundary.
- Added native checks for bidirectional settings updates, loading without
  write-back, distinct window view models, shared preference propagation, and
  preserved presentation identity when a tab moves.
- Added durable title-bar/multi-window smoke outcome files because later
  renderer callbacks can overwrite transient window-title results.
- Made the multi-window fixture select another tab, wait for inactivity, and
  allow native visibility to settle before requiring actual suspension.
  Production suspension behavior was not changed; its independent suite passed.
- Preserved the original ordering of browser subscriptions during hook
  extraction and verified cleanup of pending exports on unmount.

No test-owned desktop process remained running at completion. See
[Code organization](../CODE_ORGANIZATION.md) for the resulting folder map and
ownership conventions.
