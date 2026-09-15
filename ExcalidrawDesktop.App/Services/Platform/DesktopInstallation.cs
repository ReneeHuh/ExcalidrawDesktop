using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExcalidrawDesktop.App.Services.Logging;

namespace ExcalidrawDesktop.App.Services.Platform;

internal static class DesktopInstallation
{
    private static readonly InstallationIdentity Identity = LoadIdentity();
    // Keep the handle rooted until process termination, including final window cleanup.
    private static Mutex? runningMutex;

    private static string UserMutexSuffix =>
        Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)))).ToLowerInvariant();

    internal static string RunningMutexName => Identity.MutexPrefix + UserMutexSuffix;
    internal static string MaintenanceMutexName => Identity.MaintenanceMutexPrefix + UserMutexSuffix;

    public static bool TryHoldProcessLifetime()
    {
        using var maintenance = new Mutex(initiallyOwned: false, MaintenanceMutexName);
        bool acquired;
        try { acquired = maintenance.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return false;
        try
        {
            runningMutex ??= new Mutex(initiallyOwned: false, RunningMutexName);
            GC.KeepAlive(runningMutex);
            return true;
        }
        finally { maintenance.ReleaseMutex(); }
    }

    public static bool IsInstallerManaged(string directory)
    {
        try
        {
            var marker = Path.Combine(directory, Identity.MarkerFile);
            return File.Exists(marker) && File.ReadAllText(marker).Trim() == Identity.AppId;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning($"[DesktopInstallation] Could not read installation marker: {exception.Message}");
            return false;
        }
    }

    private static InstallationIdentity LoadIdentity()
    {
        using var stream = typeof(DesktopInstallation).Assembly.GetManifestResourceStream(
            "ExcalidrawDesktop.InstallerIdentity.json")
            ?? throw new InvalidOperationException("The installer identity resource is missing.");
        return JsonSerializer.Deserialize<InstallationIdentity>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("The installer identity resource is invalid.");
    }

    private sealed record InstallationIdentity(string AppId, string MarkerFile, string MutexPrefix, string MaintenanceMutexPrefix);
}
