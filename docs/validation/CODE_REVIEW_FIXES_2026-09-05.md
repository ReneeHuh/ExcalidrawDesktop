# Code review fixes — September 5, 2026

This implementation addresses the recommended document-safety, recovery, bridge, library, settings, and dependency fixes from [the original review](../CODE_REVIEW_2026-09-05.md). Three Luna agents worked on bounded areas; the supervising agent inspected their changes, corrected integration issues, and ran the combined checks. Existing working-tree changes were preserved and included in the validated implementation. No release was created.

## Implemented changes

| Review item | Result |
| --- | --- |
| 01 — Reload safety | A WebView may navigate to its trusted entry point once. Later reloads and internal replacements are cancelled. Navigation completion is correlated by ID so cancelling a reload does not put the existing drawing into a failed state. |
| 02 — Editor vulnerability | Upgraded Excalidraw to 0.18.1 and rebuilt the shipped web assets. |
| 03 — Recovery retention | Discover and retain orphan snapshots; failed restoration leaves its original snapshot intact. Failed initial recovery gets a fresh tab identity. Discovery errors disable pruning. Retention is evaluated after acquiring the same gate used by snapshot writes. |
| 04 — Continuous-edit recovery | Recovery uses an anchored write timer and bounded retry timing, so continued drawing cannot keep resetting the deadline. |
| 05 — Save/recovery ordering | Save completion is separate from load completion. Snapshot deletion checks the current dirty state, document version, and recovery identity under the recovery gate. Saving A while editing B preserves B's recovery and refreshes its saved-file baseline. |
| 06 — Close safety | Native commands and editor input lock during close decisions. A correlated editor acknowledgement settles operations before prompting. Document versions and load identities are rechecked across workspace persistence and pruning. |
| 07 — Exact file baselines | Read the scene and its raw-byte hash from one stable file handle. Recheck expected bytes inside the Windows write transaction before truncating. Cancellation before commit preserves the original; cancellation after commit starts cannot report that committed work was rolled back. |
| 08 — Workspace validation | Validate paths, bound collections, reject malformed recovery identities, and normalize duplicate identities and document paths without throwing during restoration. |
| 09 — Recovery size | Avoid unnecessary Unicode escaping and validate the original scene separately from recovery metadata. Preserve the original file baseline across recovery serialization. |
| 10 — Path reservations | Reserve open and Save As destinations across windows while asynchronous work is in flight. Transfer ownership only when the matching load is acknowledged; release leases on failure. |
| 11 — Load protocol | Correlate native loads with UUIDs, queue loads behind saves, time out stalled work, ignore cancelled parsers' late results, and acknowledge only after the scene and dirty baseline are installed. Retry loads also carry the matching native ID. |
| 12 — PNG cancellation | Native cancellation aborts the web wait even when rendering is unresolved. Stale completions cannot clear a newer export. Native completion respects the transaction's commit boundary. |
| 13 — Library persistence | Store the library under the desktop data root. Revision-checked writes merge concurrent additions without silently replacing another tab's library. Broadcasts do not echo writes. Failures retain pending changes with an explicit retry; pending changes prevent close and tab unloading. Undo during an earlier save is preserved. |
| 14 — Modal ownership | One modal queue per window serializes native dialogs and file pickers, including close and conflict decisions. |
| 15 — Async failures | Catch operational failures at editor message, URI launch, and startup boundaries. Log the failure; startup failures show recovery guidance instead of unwinding the async launch handler. |
| 16 — Open from Settings | File > Open finds or creates a document session when Settings is selected. |
| 18 — Recovery failure feedback | Failed recovery writes show an actionable Save As status and retry automatically. A successful later write clears the failure state. |
| 19 — PNG error feedback | Distinct localized messages describe empty drawings, dimension and byte limits, invalid images, and upload failures. |
| 22 — Settings persistence | Report settings that applied only in memory, retain the failure message when Settings reopens, and provide Retry. Restart guidance appears only after the language preference is successfully persisted. |
| 24 — Dependencies | Updated editor/build/test dependencies and patched vulnerable transitive packages. Added scheduled and dependency-PR audits. The complete Yarn audit went from 40 advisory/package records to zero. |

Related fixes refresh accessible tab status after external conflicts (21) and isolate smoke processes from normal activation and file-association registration (32).

## Verification

| Check | Result |
| --- | --- |
| Web tests | 85 passed across 9 files |
| Core tests, Release | 121 passed |
| Windows transaction tests, Release | 6 passed against the production transaction writer |
| TypeScript | Passed |
| Native localization | Passed: 8 locales, 184 keys, 130 code references |
| Production web build | Passed; existing large-bundle warnings remain |
| Fresh unpackaged Debug publish | Passed with rebuilt web assets; subsequent native fixes republished |
| Native Release, warnings as errors | Passed with zero warnings/errors |
| Document-safety Windows smoke | Passed: reload preservation, save/bridge isolation, external change/move/delete, Reload/Save As/Keep Editing |
| Close-decision Windows smoke | Passed: Cancel, Discard, Save, window Cancel, Review, and Save All |
| Tabbed Windows smoke | Passed: storage/scene/undo isolation, file and recovery retries, dirty tracking, close timeouts, save A/edit B recovery, cancellation/retry, and editor failure during PNG export |
| Crash-recovery Windows smoke | Passed: two dirty untitled drawings restored after forced termination and validated by the editor |
| PNG Windows smoke | Passed: 14,400 × 3,880 image, embedded images, binary transfer, and transactional write |
| Multi-window Windows smoke | Passed: live-tab moves, identity and watcher preservation, unsafe-move rejection, save/Save As isolation, and recovery after editor failure |
| Multi-window exit, clean and dirty | Both passed: process exit and aggregate workspace persistence; dirty-window discard also verified |
| Yarn audit | Zero advisories; [saved result](CODE_REVIEW_DEPENDENCY_AUDIT_FIXED_2026-09-05.json) |
| NuGet audit, app plus transitives | No vulnerable packages reported by the configured sources |
| Whitespace check | Passed |

The added coverage for items 30–31 includes mounted React tests using the real bridge transport, save/load overlap, save A/edit B, cancellation and retry, close locking, library concurrency, and actual Windows transaction fault cases. Core tests cover navigation admission, close commit checks, reservations, recovery retention, serialization, and exact baselines. Native smoke tests exercise the published WebView2 application.

The eight successful Windows smoke outputs are saved in [CODE_REVIEW_WINDOWS_SMOKE_2026-09-05.json](CODE_REVIEW_WINDOWS_SMOKE_2026-09-05.json).

## Remaining review work

The entire 41-item review is not marked complete. Deferred work includes saved-window restoration behavior (17), the remaining localization cleanup (20), data-root validation (23), broader page hardening and size/performance budgets (25–29), further native-controller test extraction and browser-harness cleanup (30–31), publish verification in CI and build packaging improvements (33–35), architecture/toolchain/backlog cleanup (36–39), the real-device release matrix (40), and diagnostic retention (41).

These checks do not replace installer, assistive-technology, pen/touch, cloud/network filesystem, large-document performance, or cross-application interoperability testing. The dependency audit describes the registry's known advisories at validation time.
