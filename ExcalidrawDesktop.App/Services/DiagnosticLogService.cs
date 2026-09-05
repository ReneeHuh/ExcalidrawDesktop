using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ExcalidrawDesktop.App.Services;

internal static class DiagnosticLogService
{
    private const long MaximumLogBytes = 2 * 1024 * 1024;
    private const int RetainedArchives = 5;
    private const int MaximumBatchRecords = 64;
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);
    private static readonly object Gate = new();
    private static readonly string RunId = Guid.NewGuid().ToString("N");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly string[] RedactedRoots = BuildRedactedRoots();
    private static readonly Channel<LogRecord> Records =
        Channel.CreateUnbounded<LogRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private static long sequence;
    private static bool initialized;
    private static Task? writerTask;

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
            EnsureWriter();
            Enqueue("info", "application.started", new
            {
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
                osVersion = Environment.OSVersion.VersionString,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                logDirectory = Redact(DirectoryPath),
            }, null, null);
            if (DesktopPaths.DataRootOverrideError is { } overrideError)
            {
                Enqueue("error", "application.data_root_override_ignored", new
                {
                    message = overrideError,
                    dataRoot = Redact(DesktopPaths.DataRoot),
                }, null, null);
            }
        }
    }

    public static void Info(string eventName, object? data = null) =>
        Write("info", eventName, data, null, waitForWrite: false);

    /// <summary>
    /// Error records are written before returning (bounded by a short timeout)
    /// so crash handlers can rely on them reaching disk.
    /// </summary>
    public static void Error(
        string eventName,
        Exception exception,
        object? data = null) =>
        Write("error", eventName, data, exception, waitForWrite: true);

    /// <summary>
    /// Blocks until every record queued so far has been written, bounded by a
    /// short timeout. Call before terminating the process.
    /// </summary>
    public static void Flush()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Records.Writer.TryWrite(new LogRecord(null, completion)))
        {
            return;
        }

        try
        {
            completion.Task.Wait(FlushTimeout);
        }
        catch (AggregateException)
        {
        }
    }

    private static void Write(
        string level,
        string eventName,
        object? data,
        Exception? exception,
        bool waitForWrite)
    {
        lock (Gate)
        {
            initialized = true;
            EnsureWriter();
        }

        var completion = waitForWrite
            ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        Enqueue(level, eventName, data, exception, completion);
        if (completion is null)
        {
            return;
        }

        try
        {
            completion.Task.Wait(FlushTimeout);
        }
        catch (AggregateException)
        {
        }
    }

    private static void Enqueue(
        string level,
        string eventName,
        object? data,
        Exception? exception,
        TaskCompletionSource? completion)
    {
        string line;
        try
        {
            var record = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                sequence = Interlocked.Increment(ref sequence),
                runId = RunId,
                level,
                eventName,
                data,
                exception = exception is null ? null : Redact(exception.ToString()),
            };
            line = JsonSerializer.Serialize(record, JsonOptions);
        }
        catch
        {
            // Diagnostics must never interfere with the application action being logged.
            completion?.TrySetResult();
            return;
        }

        if (!Records.Writer.TryWrite(new LogRecord(line, completion)))
        {
            completion?.TrySetResult();
        }
    }

    private static void EnsureWriter()
    {
        writerTask ??= Task.Factory.StartNew(
            DrainAsync,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    private static async Task DrainAsync()
    {
        var reader = Records.Reader;
        var batch = new List<LogRecord>(MaximumBatchRecords);
        while (await reader.WaitToReadAsync())
        {
            while (batch.Count < MaximumBatchRecords && reader.TryRead(out var record))
            {
                batch.Add(record);
            }

            try
            {
                if (batch.Any(record => record.Line is not null))
                {
                    WriteBatch(batch);
                }
            }
            catch
            {
                // Diagnostics must not interfere with the application. Retry
                // the log location when the next batch arrives.
            }
            finally
            {
                foreach (var record in batch)
                {
                    record.Completion?.TrySetResult();
                }
                batch.Clear();
            }
        }
    }

    private static void WriteBatch(IReadOnlyList<LogRecord> records)
    {
        // Redirected launches write to the same log. Coordinate both append
        // and rotation across processes, including separate Windows sessions.
        var pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(CurrentLogPath).ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Global\ExcalidrawDesktop.Diagnostics." + pathHash);
        try
        {
            if (!mutex.WaitOne(FlushTimeout))
            {
                return;
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous process exited while writing; this thread now owns
            // the mutex and may continue with a fresh file handle.
        }

        try
        {
            AppendBatch(records);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void AppendBatch(IReadOnlyList<LogRecord> records)
    {
        LogFile? file = null;
        try
        {
            foreach (var record in records)
            {
                if (record.Line is not { } line)
                {
                    continue;
                }

                file ??= LogFile.Open(CurrentLogPath);
                if (file.Length >= MaximumLogBytes)
                {
                    file.Dispose();
                    file = null;
                    Rotate();
                    file = LogFile.Open(CurrentLogPath);
                }
                file.WriteLine(line);
            }
        }
        finally
        {
            // Flush and close before releasing the mutex: a retained append
            // handle would have a stale position after another process writes
            // or rotates the log.
            file?.Dispose();
        }
    }

    private static void Rotate()
    {
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
                File.Move(source, ArchivePath(index + 1), overwrite: true);
            }
        }
        if (File.Exists(CurrentLogPath))
        {
            File.Move(CurrentLogPath, ArchivePath(1), overwrite: true);
        }
    }

    private static string ArchivePath(int index) => Path.Combine(
        DirectoryPath,
        $"diagnostics.{index}.jsonl");

    private static string[] BuildRedactedRoots()
    {
        string Folder(Environment.SpecialFolder folder)
        {
            try
            {
                return Environment.GetFolderPath(folder);
            }
            catch (Exception exception) when (
                exception is ArgumentException or PlatformNotSupportedException)
            {
                return string.Empty;
            }
        }

        return
        [
            Folder(Environment.SpecialFolder.UserProfile),
            Folder(Environment.SpecialFolder.LocalApplicationData),
        ];
    }

    private static string Redact(string value)
    {
        // Longest roots first so a nested root is masked before its parent.
        var dataRootIsProfileRelative = RedactedRoots.Any(root =>
            !string.IsNullOrEmpty(root) &&
            DesktopPaths.DataRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        var replacements = new (string Root, string Token)[]
        {
            (dataRootIsProfileRelative ? string.Empty : DesktopPaths.DataRoot,
                "%EXCALIDRAW_DATA_ROOT%"),
            (RedactedRoots[1], "%LOCALAPPDATA%"),
            (RedactedRoots[0], "%USERPROFILE%"),
        };
        foreach (var (root, token) in replacements.OrderByDescending(pair => pair.Root.Length))
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            value = value.Replace(root, token, StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }

    private sealed record LogRecord(string? Line, TaskCompletionSource? Completion);

    private sealed class LogFile : IDisposable
    {
        private readonly FileStream stream;
        private readonly StreamWriter writer;

        private LogFile(FileStream stream)
        {
            this.stream = stream;
            writer = new StreamWriter(stream, new UTF8Encoding(false));
        }

        public long Length => stream.Length;

        public static LogFile Open(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            return new LogFile(stream);
        }

        public void WriteLine(string line)
        {
            writer.WriteLine(line);
        }

        public void Dispose() => writer.Dispose();
    }
}
