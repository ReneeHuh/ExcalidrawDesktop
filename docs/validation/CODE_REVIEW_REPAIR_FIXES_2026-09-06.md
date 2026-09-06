# Library repair and review follow-up — September 6, 2026

Addresses the 13 findings from the follow-up review alongside the earlier implementation and its validation records. No release was created for this validation.

| Findings | Resulting behavior |
| --- | --- |
| 1 | Saving over a damaged on-disk library reports `LibraryCorrupt`, separately from invalid incoming content. The web controller reloads through the repair flow, retains the in-memory library and pending edits, and keeps close acknowledgement waiting during reload. Normal conflicts continue to merge against the previous baseline, preserving local deletions and remote edits. |
| 2 | A failed repair approval reloads the shared library after a revision conflict, I/O error, or access error, returning the valid version when available. A valid broadcast received during loading can also satisfy that load. |
| 3 | Each window remembers the damaged revision whose repair was declined. Later tab loads do not repeat that prompt. An explicit web Retry sends `retryRepair`, permitting another decision; a different damaged revision can also prompt. |
| 4 | The close barrier receives the actual tab/window close scope, including when either operation targets one tab. Export wait messages use the appropriate existing resource. |
| 5 | Window titles and accessible tab names distinguish unavailable files and unknown file versions. Four additional resource keys are translated in all eight locales. |
| 6 | Embedded recovery metadata is deserialized from the already-parsed JSON document, retaining the original path and stamp validation. |
| 7 | Snapshot metadata rewriting can return UTF-8 memory directly to the atomic byte writer, eliminating the output string conversion and re-encoding. |
| 8 | Ordinary saves retain the final file verification and transaction hash check while removing the earlier redundant verification. |
| 9 | External-file dialogs use one state switch for their title, content, primary button, and action. Shared message helpers supply both dialogs and document error mapping. |
| 10 | The per-window modal coordinator owns root resolution after queuing and retains the disposed-window guard. Caller lambdas and unused host interface members were removed. |
| 11 | Both asynchronous document hash sites share the stream hash helper, preserving cancellation and the stamp short-circuit before opening a file. |
| 12 | Redundant recovery-load I/O catches were removed. The `usedPaths` set continues to prevent duplicate restored file associations. |
| 13 | Library requests carry their originating session. Prompt progress is sent only to that session using the same event factory as document-save picker progress. |

## Validation

| Check | Result |
| --- | --- |
| Core tests, Release, warnings as errors | 151 passed |
| Windows transaction tests, Release, warnings as errors | 7 passed |
| Web tests | 90 passed across 9 files; the 15 library tests were rerun after the final controller adjustment |
| TypeScript | Passed |
| Native localization | Passed: 8 locales, 201 keys, 146 code references |
| Production web build | Passed; existing large-bundle warning remains |
| Native Debug and Release builds | Passed with no warnings or errors |
| Unpackaged Debug publish | Passed, including the final native smoke fixture |
| Repair and close-decision smoke | Passed: repair/cancel/retry, later-tab prompt suppression, stale approval, both export-close scopes, file-state labels, and existing close decisions |
| Document-safety smoke | Passed: save/bridge isolation, external modification/move/deletion, Reload, Save As, and Keep Editing |
| Tabbed recovery smoke | Passed: tab and IndexedDB isolation, recovery, baseline conflict protection, close timeout/retry, save cancellation, and renderer failure during export |
| Whitespace check | Passed |

The repair smoke uses a real native dialog and shared store: it corrupts a previously loaded library, cancels repair, checks a later tab load, retries, and verifies both existing and new items persist. It also completes a shared repair while an approval remains open, then checks that the stale approval returns the valid library. Export-message assertions cover both scopes with one target, and native assertions check title/accessibility file-state labels.

Saved results: [repair and close decisions](CODE_REVIEW_REPAIR_CLOSE_SMOKE_2026-09-06.json), [document safety](CODE_REVIEW_REPAIR_DOCUMENT_SMOKE_2026-09-06.json), and [tabbed recovery](CODE_REVIEW_REPAIR_TABBED_SMOKE_2026-09-06.json).

During validation, the initial repair fixture sometimes tried to add identical library elements twice; Excalidraw deduplicated the second addition, so no repair was triggered. The fixture now waits for a distinct scene element before adding the second item. Smoke profiles use short, separate temporary directories to avoid previous-run state and the IndexedDB initialization failure observed with a deeply nested profile path. No large-document latency or memory benchmarks were run.
