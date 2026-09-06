# Web dependency audit

The Web project is checked with Yarn Classic's locked dependency graph:

```powershell
corepack yarn install --frozen-lockfile --non-interactive
corepack yarn audit --json
```

The code review baseline reported 40 advisory/package records (35 unique
advisory IDs). After the Excalidraw upgrade, an interim complete audit reported
17 records (10 high, 7 moderate) across nested `nanoid` and `lodash-es` copies under Excalidraw and
its Mermaid parser. The graph also included the previously affected Vitest
3.0.6; Vitest is a development-only package and the shipped app does not enable
its browser/API server, but it is now pinned to 3.2.6.

The final locked graph reports **zero advisory records**; the complete result is
recorded in [CODE_REVIEW_DEPENDENCY_AUDIT_FIXED_2026-09-05.json](CODE_REVIEW_DEPENDENCY_AUDIT_FIXED_2026-09-05.json).
`package.json` uses
exact Yarn resolutions to `nanoid@3.3.18` and `lodash-es@4.18.1`, which are the
patched registry releases covering the reported advisory ranges. The resolution
warnings about requested older ranges are intentional: the resolved APIs remain
compatible with the Excalidraw 0.18.1 and Mermaid 2.2.2 callers, and the full
TypeScript and Vitest suites pass after installation.

The audit workflow runs on dependency pull requests and weekly, uploads the JSON
report even when Yarn returns a vulnerability exit code, and keeps findings
visible rather than suppressing them.
