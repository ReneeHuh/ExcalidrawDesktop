using System.Text.Json;
using ExcalidrawDesktop.App.Services.Platform;
using Xunit;

namespace ExcalidrawDesktop.Native.Tests;

public sealed class DesktopInstallationTests
{
    [Fact]
    public void PortableAndForeignMarkersDoNotDisablePortableRegistration()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"desktop-installation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Assert.False(DesktopInstallation.IsInstallerManaged(directory));
            using var stream = typeof(DesktopInstallation).Assembly.GetManifestResourceStream(
                "ExcalidrawDesktop.InstallerIdentity.json")!;
            using var identity = JsonDocument.Parse(stream);
            var marker = Path.Combine(directory, identity.RootElement.GetProperty("markerFile").GetString()!);
            File.WriteAllText(marker, "another-installer");
            Assert.False(DesktopInstallation.IsInstallerManaged(directory));
            File.WriteAllText(marker, identity.RootElement.GetProperty("appId").GetString());
            Assert.True(DesktopInstallation.IsInstallerManaged(directory));
            File.Delete(marker);
        }
        finally { Directory.Delete(directory); }
    }

    [Fact]
    public void RunningMutexRemainsDetectableAfterCollection()
    {
        Assert.True(DesktopInstallation.TryHoldProcessLifetime());
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.True(Mutex.TryOpenExisting(DesktopInstallation.RunningMutexName, out var mutex));
        mutex!.Dispose();
    }

    [Fact]
    public void MaintenanceBlocksNewStartupUntilReleased()
    {
        using var maintenance = new Mutex(false, DesktopInstallation.MaintenanceMutexName);
        Assert.True(maintenance.WaitOne(1000));
        try
        {
            bool? allowed = null;
            Exception? failure = null;
            var startup = new Thread(() =>
            {
                try { allowed = DesktopInstallation.TryHoldProcessLifetime(); }
                catch (Exception exception) { failure = exception; }
            });
            startup.Start();
            Assert.True(startup.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.Equal(false, allowed);
        }
        finally { maintenance.ReleaseMutex(); }
        Assert.True(DesktopInstallation.TryHoldProcessLifetime());
    }
}
