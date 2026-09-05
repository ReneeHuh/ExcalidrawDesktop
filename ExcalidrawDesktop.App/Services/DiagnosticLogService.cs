using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ExcalidrawDesktop.App.Services;

internal static class DiagnosticLogService
{
    private const long MaximumLogBytes = 2 * 1024 * 1024;
    private const int RetainedArchives = 5;
    private static readonly object Gate = new();
    private static readonly string RunId = Guid.NewGuid().ToString("N");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static long sequence;
    private static bool initialized;

    public static string DirectoryPath { get; } =
        DesktopPaths.DiagnosticsDirectory;

    private static string CurrentLogPath => Path.Combine(
        DirectoryPath,
        "diagnostics.jsonl");

    public static void Initialize()
    {
        lock (Gate)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            WriteCore("info", "application.started", new
            {
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                osVersion = Environment.OSVersion.VersionString,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                logDirectory = "%LOCALAPPDATA%\\ExcalidrawDesktop\\Diagnostics",
            });
        }
    }

    public static void Info(string eventName, object? data = null) =>
        Write("info", eventName, data, null);

    public static void Error(
        string eventName,
        Exception exception,
        object? data = null) =>
        Write("error", eventName, data, exception);

    private static void Write(
        string level,
        string eventName,
        object? data,
        Exception? exception)
    {
        lock (Gate)
        {
            initialized = true;
            WriteCore(level, eventName, data, exception);
        }
    }

    private static void WriteCore(
        string level,
        string eventName,
        object? data,
        Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            RotateIfNeeded();
            var record = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                sequence = ++sequence,
                runId = RunId,
                level,
                eventName,
                data,
                exception = exception is null ? null : Redact(exception.ToString()),
            };
            File.AppendAllText(
                CurrentLogPath,
                JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never interfere with the application action being logged.
        }
    }

    private static void RotateIfNeeded()
    {
        var current = new FileInfo(CurrentLogPath);
        if (!current.Exists || current.Length < MaximumLogBytes)
        {
            return;
        }

        var oldest = ArchivePath(RetainedArchives);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }
        for (var index = RetainedArchives - 1; index >= 1; index--)
        {
            var source = ArchivePath(index);
            if (File.Exists(source))
            {
                File.Move(source, ArchivePath(index + 1));
            }
        }
        File.Move(CurrentLogPath, ArchivePath(1));
    }

    private static string ArchivePath(int index) => Path.Combine(
        DirectoryPath,
        $"diagnostics.{index}.jsonl");

    private static string Redact(string value)
    {
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return value
            .Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
            .Replace(localAppData, "%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase);
    }
}
