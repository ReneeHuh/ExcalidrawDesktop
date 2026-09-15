using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ExcalidrawDesktop.App.Services.Logging;
using Xunit;

namespace ExcalidrawDesktop.Native.Tests;

[CollectionDefinition("Logging", DisableParallelization = true)]
public sealed class LoggingCollection;

[Collection("Logging")]
public sealed class LoggingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"desktop-logs-{Guid.NewGuid():N}");
    private readonly StringWriter output = new();
    private readonly TextWriterTraceListener listener;
    private readonly LogLevel originalMinimum = AppLogger.MinimumLevel;

    public LoggingTests()
    {
        listener = new TextWriterTraceListener(output);
        Trace.Listeners.Add(listener);
        AppLogger.MinimumLevel = LogLevel.Debug;
    }

    [Fact]
    public void EntryMatchesReadPleaseFormatAndCapturesTheCallSite()
    {
        var expectedLine = CurrentLine() + 1;
        AppLogger.Info("[LoggingTests] Drawing saved");
        var text = output.ToString();
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[INFO\] \[Thread:\d+\] ", text);
        Assert.EndsWith($"[LoggingTests] Drawing saved | {nameof(EntryMatchesReadPleaseFormatAndCapturesTheCallSite)} (line {expectedLine}){Environment.NewLine}", text);
        Assert.EndsWith(text, AppLogger.CurrentLog);
    }

    [Fact]
    public void MinimumLevelFiltersBothTraceAndSessionHistory()
    {
        var hidden = Guid.NewGuid().ToString();
        var visible = Guid.NewGuid().ToString();
        AppLogger.MinimumLevel = LogLevel.Warning;
        AppLogger.Debug(hidden);
        AppLogger.Info(hidden);
        AppLogger.Warning(visible);
        AppLogger.Critical(visible);
        Assert.DoesNotContain(hidden, output.ToString());
        Assert.DoesNotContain(hidden, AppLogger.CurrentLog);
        Assert.Contains("[WARNING]", output.ToString());
        Assert.Contains("[CRITICAL]", output.ToString());
        Assert.Contains(visible, AppLogger.CurrentLog);
    }

    [Fact]
    public void ExceptionIncludesTypeMessageAndStackWithPathRedaction()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Exception failure;
        try { throw new IOException($"Could not open {Path.Combine(profile, "drawing.excalidraw")}"); }
        catch (IOException exception) { failure = exception; }
        AppLogger.Error("[LoggingTests] Open failed", failure);
        Assert.Contains("[ERROR]", output.ToString());
        Assert.Contains("Exception: IOException - Could not open", output.ToString());
        Assert.Contains("%USERPROFILE%", output.ToString());
        Assert.Contains(nameof(ExceptionIncludesTypeMessageAndStackWithPathRedaction), output.ToString());
        Assert.DoesNotContain(profile, output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConcurrentEntriesStayCompleteAndOrderedInTraceAndHistory()
    {
        var marker = Guid.NewGuid().ToString("N");
        var start = AppLogger.CurrentLog.Length;
        Parallel.For(0, 100, index => AppLogger.Debug($"[LoggingTests] {marker}:{index}"));
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(100, lines.Length);
        Assert.Equal(100, lines.Select(line => Regex.Match(line, marker + @":\d+").Value).Distinct().Count());
        Assert.Equal(output.ToString(), AppLogger.CurrentLog[start..]);
    }

    [Fact]
    public void BrokenTraceListenerDoesNotInterruptTheCallerOrLoseHistory()
    {
        var broken = new ThrowingListener();
        Trace.Listeners.Add(broken);
        try
        {
            var marker = Guid.NewGuid().ToString();
            AppLogger.Info(marker);
            Assert.Contains(marker, AppLogger.CurrentLog);
        }
        finally { Trace.Listeners.Remove(broken); }
    }

    [Fact]
    public void SimultaneousLogFilesNeverOverwriteAnotherRun()
    {
        var paths = new string[12];
        Parallel.For(0, paths.Length, index =>
        {
            using var stream = LogFile.Create(directory, "AppLog");
            paths[index] = stream.Name;
            using var writer = new StreamWriter(stream);
            writer.Write(index.ToString());
        });
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        for (var index = 0; index < paths.Length; index++)
        {
            Assert.Matches(@"^AppLog_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(_\d+)?\.txt$", Path.GetFileName(paths[index]));
            Assert.Equal(index.ToString(), File.ReadAllText(paths[index]));
        }
    }

    [Fact]
    public void CrashReportContainsExceptionDetailsAndTheSessionHistory()
    {
        var marker = Guid.NewGuid().ToString();
        AppLogger.Info($"[LoggingTests] Before crash {marker}");
        var failure = new InvalidOperationException("Outer failure", new IOException("Inner failure"));
        var path = CrashLogger.WriteReport(failure, directory);
        Assert.NotNull(path);
        Assert.StartsWith("Crash_", Path.GetFileName(path));
        var report = File.ReadAllText(path);
        Assert.Contains("Time: ", report);
        Assert.Contains("App: Version ", report);
        Assert.Contains("Is64BitOS: ", report);
        Assert.Contains("Inner Exception:", report);
        Assert.Contains("Inner failure", report);
        Assert.Contains("============================", report);
        Assert.Contains(marker, report);
        Assert.Contains("[Unhandled Error] Fatal Error Crash: Outer failure", report);
    }

    [Fact]
    public void UnwritableCrashDirectoryDoesNotReplaceTheOriginalFailure()
    {
        Directory.CreateDirectory(directory);
        var blockedPath = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blockedPath, "keep");
        Assert.Null(CrashLogger.WriteReport(new IOException("Original failure"), blockedPath));
        Assert.Equal("keep", File.ReadAllText(blockedPath));
        Assert.Contains("Original failure", AppLogger.CurrentLog);
    }

    private static int CurrentLine([CallerLineNumber] int line = 0) => line;

    public void Dispose()
    {
        Trace.Listeners.Remove(listener);
        listener.Dispose();
        output.Dispose();
        AppLogger.MinimumLevel = originalMinimum;
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private sealed class ThrowingListener : TraceListener
    {
        public override void Write(string? message) => throw new IOException("Test listener failed");
        public override void WriteLine(string? message) => throw new IOException("Test listener failed");
    }
}
