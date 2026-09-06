# Full project code review — September 5, 2026

Implementation follow-up: [recommended fixes and validation results](validation/CODE_REVIEW_FIXES_2026-09-05.md). The findings and original check results below are retained as the review baseline.

The main priorities are document safety and dependency security. The current code compiles and its unit tests pass, but those checks do not exercise several consequential interactions between the editor, native file operations, recovery, and closing.

This report contains **41 actionable items**, including defects, source-level reliability concerns, cleanup, and validation work. It reviews the **current working tree, including the existing modified and untracked implementation files**, rather than only the last commit. No application code was changed for this review.

P1 means address first because of possible lost work or a relevant security vulnerability. P2 means significant reliability, usability, testing, or release work. P3 means lower-priority cleanup or refinement. **Reproduced** means an isolated check demonstrated the behavior; **source-traced** means the relevant execution paths were inspected but the full UI scenario was not executed; **improvement** and **validation** are recommendations, not claims of demonstrated bugs.

## Checks performed

| Check | Result |
| --- | --- |
| C# core tests, Release | 100 passed, 0 failed |
| Web unit tests | 61 passed across 6 files |
| TypeScript | Passed |
| Native localization validator | Passed: 8 locales, 176 keys, 125 code references |
| Native Debug x64 build, warnings as errors | Passed, 0 warnings/errors |
| Native Release x64 build, warnings as errors | Passed, 0 warnings/errors |
| Production web build | Passed; large-chunk warnings remain |
| NuGet audit, application plus transitives | No known vulnerable packages reported by the configured sources |
| Yarn audit | 40 advisory/package records: 1 critical, 6 high, 28 moderate, 5 low; 35 distinct advisory IDs |
| Continuous-edit recovery check | Reproduced 60 seconds of edits with zero snapshot writes |
| Invalid workspace path check | Reproduced acceptance followed by `ArgumentException` during path normalization |
| Unreadable workspace plus pruning | Reproduced deletion of an otherwise valid recovery snapshot |
| Recovery serialization expansion | Reproduced a 2,071-byte document becoming a 6,094-byte snapshot |

The native builds used the existing generated `Assets/Web` folder; the production web bundle was also built separately. A fresh publish, native UI smoke suites, installer behavior, assistive technology, actual device input, and fault injection against Windows file transactions were **not** run. This is a broad code review, not a guarantee that every defect has been found. The dependency audit identifies affected versions; it does not establish exploitability of every transitive advisory in this application.

## Address first

### 01 — P1 — Browser reload can erase the editor while retaining its native file target

**Source-traced.** [EditorSessionController.cs:522](../ExcalidrawDesktop.App/Services/EditorSessionController.cs#L522), [navigation policy:736](../ExcalidrawDesktop.App/Services/EditorSessionController.cs#L736), [main.tsx:90](../ExcalidrawDesktop.Web/src/main.tsx#L90).

Browser accelerator keys and default context menus are enabled. Same-origin navigation is accepted, and a successful reload does not stage restoration of the current scene. After an opened drawing has cleared `PendingDocumentLoad`, F5/Ctrl+R can create an empty React editor while `DocumentService` still owns the original file. Saving that empty editor can overwrite the drawing. Unsaved canvas state also disappears without a close decision. Microsoft documents the reload keys controlled by this setting in its [WebView2 accelerator reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.arebrowseracceleratorkeysenabled?view=webview2-dotnet-1.0.4022.49).

**Fix:** disable or intercept browser reload/navigation commands and route intentional reloads through the document lifecycle. Invalidate readiness on navigation and correlate the new page with restored document state. **Regression:** F5, Ctrl+R, hard reload, and reload context actions on both saved and unsaved drawings; assert scene preservation and unchanged backing files.

### 02 — P1 — The pinned editor has a confirmed Mermaid conversion XSS vulnerability

**Dependency-confirmed.** [package.json:19](../ExcalidrawDesktop.Web/package.json#L19), [yarn.lock:308](../ExcalidrawDesktop.Web/yarn.lock#L308).

The project pins `@excalidraw/excalidraw` 0.18.0. The maintainer explicitly identifies that version as affected by XSS when a user pastes an unsafe Mermaid sequence diagram; the advisory names 0.18.1 as patched. See the [Excalidraw advisory](https://github.com/excalidraw/excalidraw/security/advisories/GHSA-39h7-pwv7-rc3x). The application-specific concern is that injected script runs in a page with access to the desktop bridge. This is an inference from the bridge architecture, not a demonstrated native exploit.

**Fix:** update the editor through the existing update script, inspect the resulting Mermaid/DOMPurify dependency graph, and rerun the audit and integration checks. **Regression:** representative Mermaid imports, non-executing malicious-label fixtures, ordinary document round trips, recovery, and export.

### 03 — P1 — Recovery cleanup can destroy the only surviving drawing after a restore failure

**Reproduced at the store boundary; startup path source-traced.** [MultiWindowWorkspaceState.cs:49](../ExcalidrawDesktop.Core/MultiWindowWorkspaceState.cs#L49), [MainWindow.xaml.cs:733](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L733), [ApplicationWorkspaceCoordinator.cs:359](../ExcalidrawDesktop.App/Services/ApplicationWorkspaceCoordinator.cs#L359), [RecoverySnapshotStore.cs:49](../ExcalidrawDesktop.Core/RecoverySnapshotStore.cs#L49).

Unreadable, malformed, and unsupported workspace state all become an ordinary empty workspace. A dirty tab whose snapshot cannot be loaded is skipped. Startup then retains only recovery IDs belonging to successfully restored live dirty sessions and deletes the rest. A temporarily inaccessible snapshot, a damaged workspace index, or a newer workspace version can therefore cause permanent removal of useful recovery data. An isolated valid snapshot was deleted after loading a broken index and running the same empty-retention pruning operation.

**Fix:** distinguish missing, invalid, unsupported, and successfully loaded state; preserve or quarantine snapshots when restoration is incomplete. Offer discovery of orphan snapshots and retain a previous workspace generation. **Regression:** corrupt/missing/newer-version index, temporarily locked snapshot, and one failed tab among several valid recoveries.

### 04 — P1 — Continuous drawing can indefinitely postpone recovery

**Reproduced.** [RecoverySnapshotController.ts:19](../ExcalidrawDesktop.Web/src/document/RecoverySnapshotController.ts#L19).

Each new revision rearms a two-second trailing timer. Repeated viewport events are correctly deduplicated, but actual drawing edits arriving less than two seconds apart keep pushing the write into the future. An isolated clock test produced **zero writes during 60 successive one-second edits**; the first write happened only after a quiet interval. A crash can lose the entire continuous-edit period.

**Fix:** add a maximum dirty interval or periodic checkpoint while preserving coalescing and retry backoff. **Regression:** continuously change revisions for a minute and assert bounded snapshot age and eventual storage of the latest revision.

### 05 — P1 — Completing an older save deletes recovery for newer edits

**Source-traced.** [BridgeDispatcher.cs:328](../ExcalidrawDesktop.App/Services/BridgeDispatcher.cs#L328), [MainWindow.xaml.cs:994](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L994), [main.tsx:228](../ExcalidrawDesktop.Web/src/main.tsx#L228).

Saving captures revision A, while the editor permits subsequent edits to revision B. B can already have a durable recovery snapshot when A finishes saving. Native save completion calls `OnDocumentOpened`, which unconditionally deletes that snapshot before the web side compares its current revision with A. The web side schedules B again after its debounce. A crash in that interval loses edits that had already reached recovery storage; if the response is lost, the interval can last longer.

**Fix:** make recovery deletion conditional on an acknowledged saved revision/document generation. Retain a newer snapshot and update its file baseline safely. **Regression:** pause a save, edit again, persist the newer snapshot, complete the older save, and simulate termination before another recovery write.

### 06 — P1 — Closing needs a final document-state barrier and shared command gating

**Source-traced.** [WindowCloseController.cs:211](../ExcalidrawDesktop.App/Services/WindowCloseController.cs#L211), [MainWindow.xaml.cs:1263](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L1263), [ApplicationWorkspaceCoordinator.cs:534](../ExcalidrawDesktop.App/Services/ApplicationWorkspaceCoordinator.cs#L534).

The close flow checks dirty state, then awaits persistence and pruning before closing. Editing and several commands remain available during those awaits, and the clean/save-all branches do not revalidate the scene after them. A last edit can therefore fall outside the persisted close decision. Normal saves also lack a common close barrier. During application exit, commands in another window can still reach `CreateWindow`, which throws because `isExiting` is set.

**Fix:** establish a window/application close state that gates edits and commands, settle in-flight saves, and verify acknowledged document generations immediately before disposal. Preserve cancellation behavior. **Regression:** delay persistence/commit while attempting an edit, close, new tab, new window, Save As, and cross-window commands.

### 07 — P1 — The file baseline can describe different bytes from the document that was read or written

**Source-traced.** [DocumentService.cs:170](../ExcalidrawDesktop.App/Services/DocumentService.cs#L170), [save conflict check:364](../ExcalidrawDesktop.App/Services/DocumentService.cs#L364), [stamp reading:476](../ExcalidrawDesktop.App/Services/DocumentService.cs#L476).

Opening reads content, then obtains a fresh file stamp. An external replacement between those operations can pair old content with the replacement's timestamp/size, allowing a later save to overwrite it without detecting a conflict. Saving also checks the baseline before acquiring the write transaction, and reads its new baseline after the write helper returns. Those gaps can miss external changes. Timestamp plus length additionally misses replacements that preserve both values.

**Fix:** establish a baseline tied to the exact bytes/file identity read or committed; verify consistency around reads and compare against the expected version while holding the appropriate write protection. Consider a content fingerprint for ambiguous stamps. **Regression:** replace the file between read/stamp and check/transaction, including same-size/same-timestamp content changes.

## Other defects and reliability concerns

### 08 — P2 — Workspace validation accepts paths and recovery identities that downstream code cannot safely use

**Reproduced for malformed paths; identity consequences source-traced.** [MultiWindowWorkspaceState.cs:141](../ExcalidrawDesktop.Core/MultiWindowWorkspaceState.cs#L141), [ApplicationWorkspaceCoordinator.cs:78](../ExcalidrawDesktop.App/Services/ApplicationWorkspaceCoordinator.cs#L78), [DesktopDocumentPath.cs:5](../ExcalidrawDesktop.Core/DesktopDocumentPath.cs#L5).

The validator checks collection shapes but accepts an embedded-NUL recent path and a dirty tab with `RecoveryId: "not-a-guid"`. The coordinator normalizes recent paths outside a protective boundary; the isolated check threw `ArgumentException`. Recovery IDs are also not required to be unique across windows, so malformed state can make sessions share snapshot names.

**Fix:** validate/normalize paths safely per entry, validate and deduplicate recovery IDs globally, bound collections, and quarantine rejected entries instead of losing the whole workspace. Test malformed paths, duplicate IDs, duplicate backing files across restored windows, and invalid recovery metadata paths.

### 09 — P2 — Recovery serialization can make a supported document too large to retry

**Reproduced.** [RecoveryFileBaseline.cs:12](../ExcalidrawDesktop.Core/RecoveryFileBaseline.cs#L12), [EditorSessionController.cs:640](../ExcalidrawDesktop.App/Services/EditorSessionController.cs#L640).

Attaching metadata reparses and reserializes the entire scene with `JsonNode.ToJsonString()`, escaping non-ASCII text. A 2,071-byte fixture became 6,094 bytes. The normal document passed a scaled 3,000-byte limit, while its own snapshot failed that same limit. At production scale, a valid document under 50 MB can generate recovery rejected by Retry's 50 MB check.

**Fix:** use a recovery envelope or sidecar with a consistent payload budget, or preserve efficient UTF-8 encoding and validate the final stored representation. Test near-limit non-ASCII text and embedded-image documents through save, snapshot, startup restore, and Retry.

### 10 — P2 — Path ownership is checked without reserving in-flight opens or Save As destinations

**Source-traced.** [DocumentLifecycleController.cs:60](../ExcalidrawDesktop.App/Services/DocumentLifecycleController.cs#L60), [OpenRecentFileAsync:293](../ExcalidrawDesktop.App/Services/DocumentLifecycleController.cs#L293), [DocumentService.cs:386](../ExcalidrawDesktop.App/Services/DocumentService.cs#L386).

Path-based opens check for an existing session before awaiting I/O and do not repeat the check afterward. Two windows can both pass and attach the same drawing. Save As checks other sessions before its asynchronous write but does not reserve the destination until commit. Overlapping saves can pass ownership checks before either session records the new path.

**Fix:** coordinate path reservations across the application for both opening and saving; recheck ownership after awaits, and release reservations on every exit path. Test simultaneous cross-window opens and two Save As requests to one destination.

### 11 — P2 — Document loads need request correlation, busy handling, and completion timeouts

**Source-traced.** [main.tsx:170](../ExcalidrawDesktop.Web/src/main.tsx#L170), [DocumentLifecycleController.cs:153](../ExcalidrawDesktop.App/Services/DocumentLifecycleController.cs#L153), [DocumentService.cs:260](../ExcalidrawDesktop.App/Services/DocumentService.cs#L260).

A load event received while a document operation is running is silently dropped. Native code has already staged its file and pending content. Loads are acknowledged only by filename, and `OnDocumentOpened` also clears pending-load state when a save succeeds. There is no load ID to distinguish those operations, and the restore watchdog covers Retry rather than every ordinary load.

**Fix:** give each load a document generation/request ID, queue or explicitly reject busy requests, acknowledge only after scene application, and bound every load wait. Do not commit native file ownership until the matching editor acknowledgement. Test overlapping save/load, delayed acknowledgement, same-named files from different folders, and an editor that never completes loading.

### 12 — P2 — PNG timeout does not cancel the web operation or settle native commit coherently

**Source-traced.** [ImageExportController.cs:398](../ExcalidrawDesktop.App/Services/ImageExportController.cs#L398), [main.tsx:491](../ExcalidrawDesktop.Web/src/main.tsx#L491), [web upload:82](../ExcalidrawDesktop.Web/src/export/ImageExportController.ts#L82), [ImageExportService.cs:85](../ExcalidrawDesktop.App/Services/ImageExportService.cs#L85).

Native timeout clears `PendingImageExport` and cancels its token, but sends no cancellation event to JavaScript. A stuck render/upload promise can leave the web busy flag set indefinitely, so subsequent attempts fail as already running. A timeout after native commit starts can also report cancellation and release the native export state while the non-cancellable commit still finishes.

**Fix:** add cancellation by export ID, abort upload with an owner-window `AbortController`, invalidate late results, and keep a distinct committing state until disk outcome is known. Test hung render/upload, timeout before commit, timeout during commit, and a successful retry afterward.

### 13 — P2 — Library changes have no persistence owner

**Source-traced, including the installed editor implementation.** [main.tsx:595](../ExcalidrawDesktop.Web/src/main.tsx#L595), [EditorSessionController.cs:123](../ExcalidrawDesktop.App/Services/EditorSessionController.cs#L123).

The Excalidraw component exposes its library but this host supplies no library persistence callback, initial library data, or library adapter. Library items are separate from drawing serialization. Adding/importing library items therefore does not protect them through the drawing dirty flag, and a clean-tab unload or application restart loses that in-memory library.

**Fix:** persist a clearly owned app-wide or per-document library, or explicitly limit the feature until persistence exists. Test adding/importing items, closing, hibernation, and cross-window behavior.

### 14 — P2 — Modal dialogs and file pickers need one coordinator per window

**Source-traced.** [DocumentLifecycleController.cs:41](../ExcalidrawDesktop.App/Services/DocumentLifecycleController.cs#L41), [external conflict dialog:392](../ExcalidrawDesktop.App/Services/DocumentLifecycleController.cs#L392), [WindowCloseController.cs:164](../ExcalidrawDesktop.App/Services/WindowCloseController.cs#L164), [DocumentService.cs:496](../ExcalidrawDesktop.App/Services/DocumentService.cs#L496).

Opening/export share one flag, Save As maintains another, and close/conflict prompts use per-session flags. Selecting an inactive conflicted tab during its close flow can initiate a conflict prompt through selection handling while the close path starts another dialog. Competing modal operations can fail or silently cancel the requested action.

**Fix:** serialize dialogs/pickers per XAML root, revalidate session ownership after they close, and share that state with move/close commands. Test closing an inactive dirty conflicted tab and overlapping picker/error/close flows.

### 15 — P2 — Some async event boundaries still allow operational exceptions to escape

**Source-traced.** [EditorSessionController.cs:732](../ExcalidrawDesktop.App/Services/EditorSessionController.cs#L732), [OnNewWindowRequested:845](../ExcalidrawDesktop.App/Services/EditorSessionController.cs#L845), [OnWebMessageReceived:862](../ExcalidrawDesktop.App/Services/EditorSessionController.cs#L862), [MainWindow.xaml.cs:2429](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L2429), [App.xaml.cs:98](../ExcalidrawDesktop.App/App.xaml.cs#L98).

External URI handlers await launch operations without containment. The web-message handler catches protocol exceptions but not general failures thrown by event callbacks. Startup's async-void entry point also lacks a recoverable failure boundary. The global exception handler logs failures; it does not make these operations recoverable.

**Fix:** contain exceptions at async-void framework boundaries, use task-returning helpers, and present appropriate failure UI. Do not blanket-swallow programming errors. Test a throwing URI launcher, callback failure, and startup storage failure.

### 16 — P2 — File > Open does nothing while Settings is selected

**Source-traced.** [MainWindow.xaml.cs:1121](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L1121), [ActiveSession:2080](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L2080).

The Open command requires `ActiveSession`, which is null when the selected tab is Settings. The File menu still offers Open. The keyboard route uses the same helper and has the same limitation.

**Fix:** let the window open documents independently of the selected editor, using an existing drawing session or a new tab for picker ownership. Test File > Open and Ctrl+O from Settings.

### 17 — P3 — Disabling saved-tab restoration still recreates empty previous windows

**Source-traced UX improvement.** [App.xaml.cs:121](../ExcalidrawDesktop.App/App.xaml.cs#L121), [MainWindow.xaml.cs:713](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L713).

Every saved window is created before clean tabs are filtered by `ReopenSavedTabs`. If the last workspace had several clean windows, disabling that option can reopen several empty windows. Decide whether the preference should retain that behavior; the current UI only describes restoring drawing tabs.

**Fix:** filter restore windows after deciding which tabs are eligible, preserve windows with dirty recovery, and create one fresh window when none remain. Test a clean multi-window workspace with restoration disabled.

### 18 — P2 — Recovery write failures are invisible to the user

**Source-traced.** [RecoverySnapshotController.ts:78](../ExcalidrawDesktop.Web/src/document/RecoverySnapshotController.ts#L78), [DocumentLifecycleController.cs:534](../ExcalidrawDesktop.App/Services/DocumentLifecycleController.cs#L534), [MainWindow.xaml.cs:2368](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L2368).

Failures are retried and logged, but neither side exposes a recovery-failed state. The status bar can continue showing an old recovery time while new work is unprotected. Disk-full or access-denied failures can persist for the entire session.

**Fix:** report recovery health and last successful revision/time, with a nonmodal retry or Save As action when protection is unavailable. Test persistent failure followed by successful recovery and verify that status clears correctly.

### 19 — P2 — PNG errors discard the actionable reason

**Source-traced.** [web ImageExportController.ts:32](../ExcalidrawDesktop.Web/src/export/ImageExportController.ts#L32), [main.tsx:503](../ExcalidrawDesktop.Web/src/main.tsx#L503), [DesktopStrings.ts:243](../ExcalidrawDesktop.Web/src/localization/DesktopStrings.ts#L243).

Empty scenes, excessive dimensions, invalid blobs, and excessive PNG bytes throw ordinary `Error` objects. The caller only maps a code for `DesktopBridgeError`, so all these useful explanations become the generic export-failed message.

**Fix:** use stable export error codes with localized messages. Test the actual displayed message for empty, too-wide, too-large, invalid-image, and upload-failure cases.

### 20 — P3 — User-facing strings still bypass localization

**Source-traced.** [MainWindow.xaml.cs:759](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L759), [jump-list group:927](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L927), [ImageExportService.cs:24](../ExcalidrawDesktop.App/Services/ImageExportService.cs#L24), [ImageExportService.cs:48](../ExcalidrawDesktop.App/Services/ImageExportService.cs#L48).

Examples include “Recovered drawing,” “Recent drawings,” “PNG image,” and native export validation messages displayed directly from protocol exceptions. Passing catalog checks does not detect arbitrary English literals outside resource lookups.

**Fix:** route displayed strings through the catalogs and add a targeted scan/review for UI literals. Verify export/recovery/jump-list failure paths in a non-English locale.

### 21 — P2 — External-conflict changes do not refresh the tab's accessible state

**Source-traced.** [DocumentLifecycleController.cs:350](../ExcalidrawDesktop.App/Services/DocumentLifecycleController.cs#L350), [MainWindow.xaml.cs:2214](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L2214).

The external-file check updates `ExternalFileState` and sometimes the window title, but does not call `UpdateTabHeader`. That method owns the accessible tab name including “changed on disk.” Narrator metadata can remain stale until a separate dirty/lifecycle event happens.

**Fix:** refresh tab accessibility on every conflict-state transition and test active and inactive tabs. Also keep background editor lifecycle title updates scoped to the active document rather than allowing an inactive tab's failure to replace the window title.

### 22 — P2 — Failed settings persistence is presented as a successful saved preference

**Source-traced.** [ApplicationWorkspaceCoordinator.cs:44](../ExcalidrawDesktop.App/Services/ApplicationWorkspaceCoordinator.cs#L44), [SettingsPage.xaml.cs:52](../ExcalidrawDesktop.App/Pages/SettingsPage.xaml.cs#L52).

Preferences are applied in memory and save failures are only logged. The settings page can say a language change will apply after restart even though it was never persisted.

**Fix:** distinguish applied-in-memory from persisted settings and show a retryable save failure. Test a read-only settings location and restart-to-apply language behavior.

### 23 — P3 — Data-root validation accepts relative paths despite requiring absolute paths

**Source-traced.** [DesktopPaths.cs:35](../ExcalidrawDesktop.App/Services/DesktopPaths.cs#L35).

`GetFullPath` runs before `IsPathRooted`, making a relative override absolute before it is checked. An override such as `data` is accepted and changes meaning with the process working directory.

**Fix:** validate the supplied string with `Path.IsPathFullyQualified` before normalization, or explicitly support and document relative paths. Test relative, drive-relative, UNC, quoted, and malformed values.

## Hardening, cleanup, and validation

### 24 — P2 — Resolve the remaining dependency audit and make it continuous

**Audit-confirmed versions; reachability requires triage.** [package.json:23](../ExcalidrawDesktop.Web/package.json#L23), [yarn.lock:1591](../ExcalidrawDesktop.Web/yarn.lock#L1591), [.github/workflows/ci.yml](../.github/workflows/ci.yml).

The complete audit snapshot is linked below. Besides the direct editor issue, it reports affected Mermaid, DOMPurify, nanoid, uuid, and Vitest versions. Vitest 3.0.6 has a critical advisory involving its UI/API/browser server; this project's checked-in `vitest run` configuration does not enable that interface, so the audit is not evidence of a critical vulnerability in the shipped desktop app. See the [Vitest maintainer advisory](https://github.com/vitest-dev/vitest/security/advisories/GHSA-5xrq-8626-4rwp).

**Action:** update compatible dependencies, check all transitive copies, document any justified non-reachable exceptions, and add dependency-update/audit automation. Preserve exact version pinning and verify the final lockfile rather than assuming the editor upgrade clears every advisory.

### 25 — P2 — Add defense in depth around the privileged editor page

**Improvement.** [index.html](../ExcalidrawDesktop.Web/index.html), [EditorSessionController.cs:506](../ExcalidrawDesktop.App/Services/EditorSessionController.cs#L506), [BridgeDispatcher.cs:82](../ExcalidrawDesktop.App/Services/BridgeDispatcher.cs#L82).

There is no content security policy. The source/origin checks and export request correlation are useful, but script executing inside the trusted editor origin can invoke its bridge. Define allowed scripts, frames, network destinations, navigation paths, downloads, and permission behavior, accounting for intentional editor features and local fonts. Keep the bridge limited to necessary methods and user-visible capabilities. Validate the policy against actual Mermaid, image, clipboard, and export workflows; this is defense in depth alongside item 02.

### 26 — P2 — Bound loaded state and move large JSON work off the UI thread

**Improvement.** [BridgeProtocol.cs:27](../ExcalidrawDesktop.Core/BridgeProtocol.cs#L27), [RecoverySnapshotStore.cs:18](../ExcalidrawDesktop.Core/RecoverySnapshotStore.cs#L18), [MultiWindowWorkspaceState.cs:49](../ExcalidrawDesktop.Core/MultiWindowWorkspaceState.cs#L49), [RecoveryFileBaseline.cs:12](../ExcalidrawDesktop.Core/RecoveryFileBaseline.cs#L12).

Incoming messages permit 128 million characters; parsing/cloning, document validation, and recovery reserialization happen synchronously on native callback paths. Snapshot and workspace reads allocate the entire file without a read limit. Large supported drawings can create several simultaneous strings/JSON trees.

**Action:** enforce consistent byte limits before loading/allocating, cap state/tab counts, bound concurrent payload processing, and perform CPU-heavy validation/encoding off the dispatcher while keeping UI/WinRT access correctly marshalled. Measure UI latency and peak memory with near-limit documents.

### 27 — P2 — PNG limits need a pixel-area budget and stronger native validation

**Improvement.** [ImageExportPolicy.cs:7](../ExcalidrawDesktop.Core/ImageExportPolicy.cs#L7), [web ImageExportController.ts:35](../ExcalidrawDesktop.Web/src/export/ImageExportController.ts#L35), [ImageExportService.cs:59](../ExcalidrawDesktop.App/Services/ImageExportService.cs#L59).

16,384 × 16,384 is allowed by the dimension checks and needs **1 GiB for one RGBA surface**, before temporary rendering buffers. The compressed-byte limit is checked after rendering. Native validation checks the PNG signature but not dimensions or structural completeness; a signature-only payload passes its final signature/length condition.

**Action:** enforce a total-pixel/memory budget before rendering, offer a smaller scale, and validate at least PNG structure/dimensions before commit. Test extreme square dimensions, sparse wide drawings, truncated files, and cancellation near resource limits.

### 28 — P2 — Dirty/recovery revision calculation scans the whole scene on every change

**Improvement; performance impact not benchmarked in this review.** [DocumentRevision.ts:9](../ExcalidrawDesktop.Web/src/document/DocumentRevision.ts#L9), [main.tsx:600](../ExcalidrawDesktop.Web/src/main.tsx#L600).

Every change notification filters/maps all elements and JSON-serializes the revision, including selection and viewport activity. That work scales with scene size even when document content did not change.

**Action:** profile large scenes, coalesce non-document notifications, and cache/incrementally maintain document revision information where safe. Keep exact checks at save/close boundaries and preserve deletion, ordering, and saved-app-state coverage.

### 29 — P2 — Establish memory/startup budgets for retained content and editor bundles

**Improvement with build evidence.** [EditorSessionController.cs:175](../ExcalidrawDesktop.App/Services/EditorSessionController.cs#L175), [DocumentController.ts:86](../ExcalidrawDesktop.Web/src/document/DocumentController.ts#L86), [vite.config.mts](../ExcalidrawDesktop.Web/vite.config.mts).

Hibernation retains full drawing JSON in the native process; restored inactive tabs retain pending content; loading into an existing editor adds files without explicitly replacing its file store. These choices can retain substantial data across many image-heavy tabs. The production build emitted an approximately 1.37 MB entry and chunks up to 1.82 MB before gzip.

**Action:** measure process-tree memory and startup on the minimum machine, budget live and cached tabs, release obsolete image data safely, and inspect heavy optional features for lazy loading. Preserve required offline fonts/languages. Treat bundle warnings as measurements to investigate, not a reason to raise the warning threshold alone.

### 30 — P2 — Add automated native controller tests at the application boundary

**Validation gap.** [Core.Tests.csproj](../ExcalidrawDesktop.Core.Tests/ExcalidrawDesktop.Core.Tests.csproj), [DocumentLifecycleController.cs](../ExcalidrawDesktop.App/Services/DocumentLifecycleController.cs), [WindowCloseController.cs](../ExcalidrawDesktop.App/Services/WindowCloseController.cs).

The C# test project references Core only. Its 100 passing tests do not directly exercise the native dispatcher, document service, lifecycle/close/export controllers, coordinator, or settings store. The extracted host interfaces are a useful start, but constructors still require concrete WinUI/session/coordinator types.

**Action:** introduce narrow seams for editor transport, file transactions, dialogs, persistence, and clocks; run deterministic controller tests without a visible desktop. Prioritize items 03–12 and failure after disk commit. Keep a smaller real-Windows integration suite for WinRT behavior.

### 31 — P2 — Test mounted React orchestration and extract the browser smoke harness

**Validation and cleanup.** [main.tsx](../ExcalidrawDesktop.Web/src/main.tsx), [vitest.config.mts:26](../ExcalidrawDesktop.Web/vitest.config.mts#L26).

The test pattern includes only `*.test.ts`; there are no mounted tests for `DesktopApp` and its intertwined effects, refs, busy states, ready/load ordering, and save/recovery transitions. A large portion of `main.tsx` is a smoke API guarded by a query parameter, and that code is still included in production web builds.

**Action:** extract document/export hooks and a development-only smoke module, exclude it at build time, and add mounted integration tests with a fake bridge plus selected real editor behavior. Exercise StrictMode readiness, API availability, late events, and save/recovery interleavings.

### 32 — P2 — Smoke tests can change the user's file association

**Source-traced test-isolation defect.** [SmokeTestCommon.ps1:28](../SmokeTests/SmokeTestCommon.ps1#L28), [App.xaml.cs:113](../ExcalidrawDesktop.App/App.xaml.cs#L113), [file registration:212](../ExcalidrawDesktop.App/App.xaml.cs#L212).

The smoke helper isolates the data directory, but every surviving main app instance still registers the `.excalidraw` association before resolving smoke options. A smoke executable in a Debug publish folder can become the registered file-activation target. The alternate data directory does not isolate HKCU registration. The fixed single-instance key also prevents fully independent simultaneous test instances.

**Action:** disable registration for ordinary smoke runs; isolate instance identity; reserve real registration for a dedicated association test with explicit capture/restore of its registration state. This is why the current review did not launch the native smoke suites against the user's desktop.

### 33 — P2 — CI should validate the actual published application

**Validation gap.** [.github/workflows/ci.yml:50](../.github/workflows/ci.yml#L50), [Build-Desktop.ps1:59](../tools/Build-Desktop.ps1#L59), [App.csproj:46](../ExcalidrawDesktop.App/ExcalidrawDesktop.App.csproj#L46).

CI builds Debug and Release but does not publish or inspect the self-contained output. PRI/XBF copying, runtime completeness, license inclusion, and offline web assets can fail independently of compilation. Native smoke tests remain outside CI.

**Action:** publish Release in CI, validate the file manifest, retain an artifact, and run startup/document smoke checks on a suitable interactive Windows runner or scheduled release gate. Record which validations require hardware rather than implying unit tests cover them.

### 34 — P2 — Direct native builds can silently use missing or stale web assets

**Source-traced build-graph gap.** [App.csproj:37](../ExcalidrawDesktop.App/ExcalidrawDesktop.App.csproj#L37), [Build-WebAssets.ps1](../tools/Build-WebAssets.ps1).

The native project copies whatever is already under `Assets/Web`; it has no target that builds the web project or rejects stale/missing generated content. Visual Studio or direct `dotnet build/publish` can succeed while shipping an old editor or no editor. Native startup only discovers missing `index.html` at runtime.

**Action:** integrate asset generation or a generated manifest/hash freshness check into the native build/publish graph, with a deliberate fast-development opt-out. Verify a clean checkout and a changed TypeScript source through the documented Visual Studio workflow.

### 35 — P2 — Release output needs complete notices and license files

**Source-traced packaging gap.** [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md), [App.csproj](../ExcalidrawDesktop.App/ExcalidrawDesktop.App.csproj), [Update-Excalidraw.ps1:21](../tools/Update-Excalidraw.ps1#L21).

The notice covers the direct editor and says transitives must be preserved, but the publish rules do not explicitly include the project's root license/notices or generate transitive notices. The update script also changes the editor version without updating the version in `THIRD_PARTY_NOTICES.md`.

**Action:** generate a dependency/license inventory, preserve required font/npm/NuGet notices in the published folder, include the project license, and validate version consistency during dependency updates. Verify the archive contents, not only the repository files.

### 36 — P3 — Finish separating window UI from lifecycle state and test fixtures

**Cleanup.** [MainWindow.xaml.cs](../ExcalidrawDesktop.App/MainWindow.xaml.cs), [DocumentSession.cs](../ExcalidrawDesktop.App/Models/DocumentSession.cs), [MainWindow.SmokeTests.cs](../ExcalidrawDesktop.App/MainWindow.SmokeTests.cs).

`MainWindow.xaml.cs` remains 2,442 lines, the main native smoke partial is 2,428, and `DocumentSession` exposes many independently mutable lifecycle flags and completion objects. Eligibility conditions are repeated across save, suspend, unload, move, export, and close.

**Action:** extract workspace persistence/recent files and command state from the window, move fixtures/scenarios into a dedicated harness, and replace contradictory flag combinations with explicit transitions or centralized invariants. Keep UI composition in the window and avoid splitting files without improving ownership.

### 37 — P3 — Remove or consolidate the unused replacement-document bridge path

**Cleanup.** [DocumentController.ts:39](../ExcalidrawDesktop.Web/src/document/DocumentController.ts#L39), [DesktopBridge.ts:205](../ExcalidrawDesktop.Web/src/bridge/DesktopBridge.ts#L205), [BridgeDispatcher.cs:96](../ExcalidrawDesktop.App/Services/BridgeDispatcher.cs#L96), [DocumentService.cs:89](../ExcalidrawDesktop.App/Services/DocumentService.cs#L89).

The current UI uses workspace new/open events, but the older `document.new`/`document.open` replacement flow, confirmation state, bridge methods, and helper tests remain. `pendingOpenFileStamp` is assigned/cleared without being consumed, and `CloseAfterSave` is written without driving production close behavior. This enlarges the surface that must stay consistent with the tabbed lifecycle.

**Action:** prove callers with a repository search, remove unused flows/fields, and keep only explicitly supported protocol compatibility. Consolidate the C#/TypeScript method/payload definitions or add shared contract fixtures so the two sides cannot drift unnoticed.

### 38 — P3 — Add consistent static checks and reproducible toolchain settings

**Cleanup.** [.editorconfig](../.editorconfig), [package.json](../ExcalidrawDesktop.Web/package.json), [tsconfig.json](../ExcalidrawDesktop.Web/tsconfig.json), [.github/workflows/ci.yml](../.github/workflows/ci.yml).

The root EditorConfig specifies two-space indentation for everything, while C#/XAML predominantly use four. There is no configured JS lint/format command, the Vitest config is outside the explicit TypeScript include list, and the repository has no .NET SDK pin. Developer prerequisites allow a much broader Node/SDK range than CI exercises.

**Action:** add language-specific formatting, lint rules for async/hooks and unused code, include test/build configs in static checking, and pin/document the tested toolchain. Apply formatting separately from behavior fixes to keep reviews readable.

### 39 — P3 — Update the canonical backlog and validation claims to match the current architecture

**Documentation cleanup.** [DESKTOP_BACKLOG.md](DESKTOP_BACKLOG.md), [README.md](../README.md), [TITLE_BAR_ACCESSIBILITY_VALIDATION.md](validation/TITLE_BAR_ACCESSIBILITY_VALIDATION.md), [Invoke-RecoverySmokeTest.ps1:77](../SmokeTests/Invoke-RecoverySmokeTest.ps1#L77).

The backlog still discusses packaged/MSIX deployment, an older installed development package, and unchecked CI/payload-limit/diagnostic work that now exists in the working tree. It also claims exact recovery after termination during the debounce interval; the recovery script waits for the “Recovery snapshot saved” signal before killing the app, so that specific claim is stronger than the test.

**Action:** make unpackaged distribution the current baseline, mark historical evidence clearly, update completed work, and describe tests in terms of the operations they actually execute. Add a changelog/release checklist and keep optional roadmap features separate from defects.

### 40 — P2 — Complete the real-device and interoperability release matrix

**Validation, not demonstrated defects.** [TITLE_BAR_ACCESSIBILITY_VALIDATION.md](validation/TITLE_BAR_ACCESSIBILITY_VALIDATION.md), [DESKTOP_BACKLOG.md](DESKTOP_BACKLOG.md), [OnFileDrop:398](../ExcalidrawDesktop.App/MainWindow.xaml.cs#L398).

The repository already records outstanding checks for actual shortcuts/focus, Narrator, DPI transitions, contrast themes, scaling, touch, pen, IME, RTL, title-bar gestures, and many/narrow tabs. Add round trips with Excalidraw web, offline fonts/languages, cloud/network-backed files, disk-full/read-only destinations, and native image clipboard/drop behavior. The drop handlers claim all `StorageItems` before filtering to `.excalidraw`, so ordinary PNG/JPEG drops deserve an explicit integration check.

**Action:** run and record those cases with build/version/hardware details; use the outcomes to define supported limits. Also verify install/extract, moved installation folder, repair, removal, and the intended signed distribution/update process before public release.

### 41 — P3 — Bound diagnostics and reserve synchronous flushing for shutdown/crash paths

**Improvement.** [DiagnosticLogService.cs:24](../ExcalidrawDesktop.App/Services/DiagnosticLogService.cs#L24), [Error:76](../ExcalidrawDesktop.App/Services/DiagnosticLogService.cs#L76), [writer wait:222](../ExcalidrawDesktop.App/Services/DiagnosticLogService.cs#L222).

The log channel is unbounded, and every error call can synchronously wait up to two seconds, including operational failures logged from UI paths. Slow storage or a busy cross-process log mutex can turn a recoverable settings/recovery failure into visible pauses and allow queued records to grow.

**Action:** bound the queue with an explicit overflow policy and dropped-record count; make routine error reporting asynchronous and keep bounded synchronous flushes for fatal/shutdown paths. Test a blocked writer, a burst of events, and shutdown with pending logs.

## Suggested implementation sequence

1. Address items **01–07** and add regression cases alongside each fix.
2. Harden workspace/load/export handling in **08–15**, including native controller seams in **30** and React integration coverage in **31**.
3. Resolve user-facing failures and test isolation in **16–24** and **32**.
4. Make publish output reproducible and reviewable with **33–35**, then complete the measured performance/security and release validation work in **25–29** and **40**.
5. Apply structural, formatting, documentation, and logging cleanup in separate focused changes: **36–39**, **41**.

## Dependency audit appendix

The [complete machine-readable audit snapshot](validation/CODE_REVIEW_DEPENDENCY_AUDIT_2026-09-05.json) contains all 40 advisory/package/version records, dependency paths, advisory URLs, titles, and fixed-version ranges returned during this review. Multiple packages/versions can refer to the same advisory; the total is **35 unique advisory IDs**, not 40 independently demonstrated application vulnerabilities. Fixed-version strings are the registry's reported ranges at review time; validate the resolved graph after upgrades.

The direct editor advisory and the Vitest advisory were checked against their maintainers' primary advisories. Other transitive records remain audit findings requiring reachability triage; this review does not claim that each can be exploited through the desktop application.
