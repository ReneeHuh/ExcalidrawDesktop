# ReadPlease-style logging validation

Validated September 15, 2026 on Windows x64.

## Reference and result

Compared the local ReadPlease implementation in
`CommonCore/Logs/AppLog.cs`, `CommonCore/Logs/CrashLogger.cs`, and the Trace
listener initialization in `ReadPlease/App.xaml.cs`.

The replacement uses the same `AppLogger` helper names and levels, text entry
format, caller member/line metadata, Trace file/console listeners with automatic
flushing, per-run `AppLog_…txt` filenames, and `Crash_…txt` reports containing
exception details and session history. Existing diagnostic calls and
debugger-only failure messages now use this API.

The app stores `UsageLogs` and `CrashLogs` under its existing writable data
root. It preserves path redaction and synchronizes concurrent logging. Atomic
filename reservation prevents overwriting reports from simultaneous launches.
See [Logging](../LOGGING.md) for the conventions and intentional adaptations.

## Checks

| Check | Result |
| --- | --- |
| Native tests, Release | 14 passed: 6 file-transaction tests and 8 logging tests |
| Native Debug and Release builds, warnings as errors | Passed, zero warnings/errors |
| Debug self-contained publish | Passed |
| Title-bar/settings/tab smoke suite | Passed |
| Multi-window transfer smoke suite | Passed |
| Clean multi-window exit smoke suite | Passed |
| Tab suspension/unload/recreation smoke suite | Passed |
| Generated usage-file inspection | ReadPlease-style timestamps, levels, threads, component messages, and original caller locations |
| Source scan | No remaining `DiagnosticLogService` or `Debug.WriteLine` calls in native source |
| Whitespace check | `git diff --check` passed |

The logging tests cover exact entry structure, caller line/member propagation,
minimum-level filtering of both output and history, exception formatting and
path redaction, concurrent entry ordering, listener failure containment,
simultaneous file creation, crash report contents, and an unwritable crash
directory. Crash report behavior was verified through the same synchronous
writer used by the application's unhandled-exception hooks; validation did not
deliberately crash the desktop application.

Smoke runs created separate usage files in the isolated data root. The action
wrapper recorded `OnTabSelectionChanged` and its source line, rather than the
wrapper's own location. Captured native exception entries included their type,
message, and stack. New records were written to text files.

## Independent quality review

A separate subagent reviewed the migration against ReadPlease, including the
logger, crash reports, startup/shutdown integration, call sites, privacy,
tests, and documentation. Two suggested improvements were implemented:

- Install the file listener before the console listener, matching ReadPlease
  and persisting the record before console output.
- Include session IDs in session-specific editor, document recovery/conflict,
  close, export, and cleanup errors.

The follow-up review found no remaining actionable issue.
