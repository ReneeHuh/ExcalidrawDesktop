using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ExcalidrawDesktop.App.Services.Platform;

namespace ExcalidrawDesktop.App.Services.Logging;

internal static class DesktopLogging
{
    private static readonly object Gate = new();
    private static bool initialized;

    public static string? UsageLogPath { get; private set; }

    public static void Initialize()
    {
        lock (Gate)
        {
            if (initialized) return;
            initialized = true;
            Trace.AutoFlush = true;
            Exception? fileFailure = null;
            try
            {
                var stream = LogFile.Create(DesktopPaths.UsageLogs, "AppLog");
                UsageLogPath = stream.Name;
                Trace.Listeners.Add(new TextWriterTraceListener(stream, "ExcalidrawDesktop.UsageLog"));
            }
            catch (Exception exception)
            {
                fileFailure = exception;
            }
            // Match ReadPlease's listener order: persist before writing to the console.
            Trace.Listeners.Add(new ConsoleTraceListener());
            if (fileFailure is not null)
                AppLogger.Error("[App] Could not open the usage log file", fileFailure);

            var assembly = Assembly.GetExecutingAssembly();
            AppLogger.Info($"[App] Application started (Version={assembly.GetName().Version}, " +
                $"Build={assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}, " +
                $"OS={Environment.OSVersion.VersionString}, Architecture={RuntimeInformation.ProcessArchitecture}, " +
                $"ProcessId={Environment.ProcessId})");
            if (DesktopPaths.DataRootOverrideError is { } error)
                AppLogger.Warning($"[App] Data folder override ignored: {error}");
        }
    }

    public static void Flush()
    {
        try { Trace.Flush(); }
        catch { /* Logging must not block shutdown with a new exception. */ }
    }
}
