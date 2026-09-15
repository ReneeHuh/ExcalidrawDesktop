# Logging

The native app follows ReadPlease's `CommonCore/Logs/AppLog.cs` and
`CrashLogger.cs` conventions, including the Trace setup in its `App.xaml.cs`.
The implementation lives in `ExcalidrawDesktop.App/Services/Logging/`.

## Writing entries

```csharp
AppLogger.Info("[MainWindow] New tab requested");
AppLogger.Debug($"[EditorSessionController] Editor ready (SessionId={session.RecoveryId})");
AppLogger.Warning("[MainWindow] Window activation timed out during application exit");
AppLogger.Error("[DocumentLifecycleController] Open drawing failed", exception);
AppLogger.Critical("[App] Unhandled runtime exception", exception);
```

The output format matches ReadPlease:

```text
[2026-09-15 10:25:30.123] [INFO] [Thread:1] [MainWindow] New tab requested | OnAddTabButtonClick (line 64)
```

Exception entries append ` | Exception: TypeName - Message`, followed by the
stack trace. Caller method and line are supplied automatically. Wrappers such
as `MainWindow.LogAction` forward their caller attributes so the log points to
the original action handler.

Use the component's class name in brackets and describe the action, result,
or failure in plain language. Include window/session IDs and relevant counts
when needed to follow work across windows. Pass an exception as the second
argument to `Error` or `Critical`. Do not include drawing contents or bridge
payloads. Existing user-profile and data-folder path redaction applies to
messages, exceptions, and crash reports.

`AppLogger.MinimumLevel` defaults to `LogLevel.Debug`, like ReadPlease. The
levels are `Debug`, `Info`, `Warning`, `Error`, and `Critical`. Filtering applies
to both Trace output and `AppLogger.CurrentLog`, which contains the current
process's session history. Calls work in Debug and Release.

## Files and Trace listeners

`DesktopLogging.Initialize()` runs before settings load and XAML initialization.
It installs a `TextWriterTraceListener` and `ConsoleTraceListener`, and sets
`Trace.AutoFlush = true`, matching ReadPlease's setup.

| File | Contents |
| --- | --- |
| `UsageLogs/AppLog_yyyy-MM-dd_HH-mm-ss.txt` | One process's Trace output |
| `CrashLogs/Crash_yyyy-MM-dd_HH-mm-ss.txt` | Environment/version information, exception details, inner exceptions, and session history |

Both directories are below `DesktopPaths.DataRoot`, normally
`%LOCALAPPDATA%/ExcalidrawDesktop`. `EXCALIDRAW_DESKTOP_DATA_ROOT` also redirects
logs, so smoke tests keep their data isolated. ReadPlease places these folders
beside its executable; Excalidraw Desktop uses its existing writable data root.

Each launch creates a new usage file. There is no rolling-file rotation or
automatic deletion. A numeric suffix is added if two launches or crashes
reserve the same timestamp, so one process cannot overwrite another's log.
The previous `Diagnostics/*.jsonl` files are historical records; new entries
use the text files above.

`CrashLogger` writes reports synchronously from the XAML and runtime unhandled
exception handlers before the process can terminate. Unobserved task
exceptions use ordinary error entries. The existing exception-handling and
window-lifetime decisions remain in `App`.

The session buffer and Trace writes are synchronized for concurrent callers.
Logging failures are contained, including inaccessible folders and broken
listeners. `DesktopLogging.Flush()` flushes the listeners during normal exit.

## Validation

`ExcalidrawDesktop.Native.Tests/LoggingTests.cs` covers the output format,
caller metadata, filtering, exception formatting/redaction, concurrent
entries, listener failure, collision-safe filenames, and crash reports.
Run it with:

```powershell
dotnet test .\ExcalidrawDesktop.Native.Tests\ExcalidrawDesktop.Native.Tests.csproj --configuration Release
```
