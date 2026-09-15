using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using ExcalidrawDesktop.App.Services.Platform;

namespace ExcalidrawDesktop.App.Services.Logging;

internal static class CrashLogger
{
    public static void LogException(Exception exception,
        [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
        => WriteReport(exception, DesktopPaths.CrashLogs, member, line);

    internal static string? WriteReport(Exception exception, string directory,
        [CallerMemberName] string member = "", [CallerLineNumber] int line = 0)
    {
        try
        {
            AppLogger.Error($"[Unhandled Error] Fatal Error Crash: {exception.Message}", exception, member, line);
            var details = new StringBuilder();
            details.AppendLine($"Time: {DateTime.Now}");
            details.AppendLine($"App: Version {Assembly.GetEntryAssembly()?.GetName().Version}");
            details.AppendLine($"Is64BitOS: {Environment.Is64BitOperatingSystem}");
            details.AppendLine($"Is64Bit: {Environment.Is64BitProcess}");
            details.AppendLine($"IsPrivilegedProcess: {Environment.IsPrivilegedProcess}");
            details.AppendLine($"OS: {Environment.OSVersion}");
            details.AppendLine($"Build: {Environment.Version.Build}");
            details.AppendLine();
            details.AppendLine("Exception:");
            AppendExceptionDetails(details, exception);
            details.AppendLine();
            details.AppendLine("============================");
            details.AppendLine();
            details.Append(AppLogger.CurrentLog);
            using var stream = LogFile.Create(directory, "Crash");
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(LogPrivacy.Redact(details.ToString()));
            DesktopLogging.Flush();
            return stream.Name;
        }
        catch
        {
            // A crash report must never cause another crash.
            return null;
        }
    }

    private static void AppendExceptionDetails(StringBuilder details, Exception exception)
    {
        details.AppendLine($"{exception.GetType()}: {exception.Message}");
        details.AppendLine($"Source: {exception.Source}");
        details.AppendLine($"Data: {exception.Data.Count}");
        details.AppendLine($"Message: {exception.Message}");
        details.AppendLine();
        details.AppendLine("Exception:");
        details.AppendLine(exception.ToString());
        details.AppendLine();
        details.AppendLine("StackTrace:");
        details.AppendLine(exception.StackTrace);
        if (exception.InnerException is { } inner)
        {
            details.AppendLine();
            details.AppendLine("Inner Exception:");
            AppendExceptionDetails(details, inner);
        }
        else details.AppendLine("No Inner Exception");
    }
}
