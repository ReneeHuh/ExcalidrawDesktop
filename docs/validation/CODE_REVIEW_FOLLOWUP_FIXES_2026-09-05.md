# Follow-up review fixes — September 5, 2026

Two implementation batches address the confirmed defects and selected cleanup from the subsequent 32-item review. This is a follow-up to the [original implementation record](CODE_REVIEW_FIXES_2026-09-05.md), not a claim that all review work is complete. No release was created for this validation.

## Changes

| Findings | Implemented behavior |
| --- | --- |
| 1, 4 | Closing with pending library changes offers Discard / Cancel. Discard approval lasts only for that close attempt; cancelling a later drawing decision preserves the library edits. Corrupt libraries offer an explicit reset that backs up the original bytes beside the library, checks the reviewed revision, and refuses to reset a changed or valid file. Existing BOM-based library decoding is preserved. |
| 2 | Closing during a PNG export now reaches the existing wait-for-export feedback. Existing modal, move, unload, and startup admission guards remain. |
| 6, 7, 14, 26 | Renderer failure settles a pending close acknowledgement immediately. Operational bridge exceptions no longer mark a healthy renderer failed. Acknowledgements require correctly typed fields and dashed GUIDs; malformed messages fail the close attempt without poisoning renderer state. Timeout failures show feedback. |
| 3, 8, 9 | File state distinguishes missing, unavailable, and unknown-baseline files. Busy/inaccessible files offer Retry rather than Locate. Unknown recovery baselines keep overwrite protection and explain Save As / Reload. Invalid UTF-8 document decoding maps to the invalid-document error. |
| 5, 10 | Discovered orphan snapshots recover validated embedded file paths without assigning duplicate persisted paths. Invalid legacy workspace migration reports Corrupt rather than Loaded. Recovery retention remains intact. |
| 13 | Yarn audit severity gating examines its JSON summary and exit mask. Low-only findings do not fail the job; moderate-or-higher advisories and operational failures do. Five fixtures cover those distinctions. |
| 16, 18, 19 | Rejected settings changes restore the displayed preferences. Retry and new native dialogs are translated across all eight locales. Document-operation error codes have actionable mappings. PNG size errors use the request's actual limit. |
| 20 | Ordinary external checks hash a stream instead of allocating the complete file. Save preflights retain baseline, timestamp, and access checks; the transaction performs the byte hash once before truncation. A Windows regression proves a same-size/same-timestamp replacement is rejected after the metadata preflight. |
| 21, 23 | Recovery metadata is rewritten using a reader/writer without a mutable scene DOM. All incoming copies of the native metadata property are removed, including escaped names; nested scene values and numeric spellings are preserved. Library application parses each payload once. These are allocation/work reductions, not measured performance-budget claims. |
| 27–32 | Removed the unused document.open/document.opened flow, write-only CloseAfterSave field, duplicate persistence member, and duplicate export busy flag. Recent and activated paths share one open implementation and post-await ownership check. Simple message dialogs share a queue-aware helper that checks window lifetime after waiting and catches disposal/COM failures. Other unexpected exceptions remain observable. |

## Verification

| Check | Result |
| --- | --- |
| Core tests, Release, warnings as errors | 142 passed |
| Windows transaction tests, Release, warnings as errors | 7 passed |
| Web tests | 84 passed across 9 files |
| TypeScript | Passed |
| Native localization | Passed: 8 locales, 197 keys, 142 code references |
| Audit-gating fixtures | 5 passed |
| Production web build | Passed; existing large-bundle warnings remain |
| Native Release, warnings as errors | Passed |
| Unpackaged Debug publish | Passed with rebuilt web assets |
| Close-decision Windows smoke | Passed: library Cancel, library Discard followed by drawing Cancel, retained edits saved on Retry, and library Discard permitting window close; ordinary Save/Discard/Cancel, Review, and Save All also passed |
| Document-safety Windows smoke | Passed: save/bridge isolation, external modification/move/deletion, Reload/Save As/Keep Editing |
| Tabbed Windows smoke | Passed: isolation, recovery, baseline conflict protection, close timeout/retry, save cancellation/retry, and renderer failure during export |
| Whitespace check | Passed |

The web suite previously had 86 tests after the first batch. Removing the obsolete open flow retired its picker/acknowledgement tests; active load-failure, cancellation, background-baseline, and undo-reset coverage remains. The final suite contains 84 tests.

Saved smoke results: [close decisions](CODE_REVIEW_FOLLOWUP_CLOSE_SMOKE_2026-09-05.json), [document safety](CODE_REVIEW_FOLLOWUP_DOCUMENT_SMOKE_2026-09-05.json), [tabbed recovery/export](CODE_REVIEW_FOLLOWUP_TABBED_SMOKE_2026-09-05.json). The smoke scripts use their isolated data root and do not register the normal file association.

## Remaining items

- 11–12: stalled non-cancellable commits and late save-result reconciliation need dedicated fault injection and an explicit pending-commit interaction design.
- 15: the proposed missing-baseline load sequence was not established; successful loads already assign the saved revision.
- 17: picker lifetime after WinRT cancellation still needs a targeted Windows reproduction.
- 22: no process-only library cache was introduced; an invalidation/ownership policy is needed before removing disk checks.
- 24: broader polling-to-event refactoring remains separate from the targeted renderer-failure completion fix.
- 25: hash casing remains internally consistent within its existing contracts and is not a demonstrated defect.

Large-document latency/memory budgets and real network-share stalls were not measured by these checks.
