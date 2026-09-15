using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace ExcalidrawDesktop.App.Services.Logging;

public enum LogLevel
{
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
}

/// <summary>ReadPlease-style text logging shared by every window in this process.</summary>
public static class AppLogger
{
    private static readonly object Gate = new();
    private static readonly StringBuilder SessionLog = new();
    private static int minimumLevel = (int)LogLevel.Debug;

    public static string CurrentLog
    {
        get { lock (Gate) return SessionLog.ToString(); }
    }

    public static LogLevel MinimumLevel
    {
        get => (LogLevel)Volatile.Read(ref minimumLevel);
        set => Volatile.Write(ref minimumLevel, (int)value);
    }

    public static void Log(
        LogLevel level,
        string message,
        Exception? ex = null,
        [CallerMemberName] string member = "",
        [CallerLineNumber] int line = 0)
    {
        if (level < MinimumLevel) return;

        try
        {
            var exceptionPart = ex is not null
                ? $" | Exception: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}{ex.StackTrace}"
                : string.Empty;
            var entry = LogPrivacy.Redact(string.Format(
                "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] [Thread:{2}] {3} | {4} (line {5}){6}",
                DateTime.Now,
                level.ToString().ToUpperInvariant(),
                Environment.CurrentManagedThreadId,
                message,
                member,
                line,
                exceptionPart));

            // Keep complete entries in the same order in memory and Trace output.
            lock (Gate)
            {
                SessionLog.AppendLine(entry);
                Trace.WriteLine(entry);
            }
        }
        catch
        {
            // A broken listener must not interrupt the operation being logged.
        }
    }

    public static void Debug(string message,
        [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => Log(LogLevel.Debug, message, null, member, line);

    public static void Info(string message,
        [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => Log(LogLevel.Info, message, null, member, line);

    public static void Warning(string message,
        [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => Log(LogLevel.Warning, message, null, member, line);

    public static void Error(string message, Exception? ex = null,
        [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => Log(LogLevel.Error, message, ex, member, line);

    public static void Critical(string message, Exception? ex = null,
        [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => Log(LogLevel.Critical, message, ex, member, line);
}
