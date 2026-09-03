$ErrorActionPreference = "Stop"

if (-not ("DesktopSmokePackagedAppActivator" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class DesktopSmokeApplicationActivationManager {}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopSmokeApplicationActivationManager
{
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        uint options,
        out uint processId);
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

public static class DesktopSmokePackagedAppActivator
{
    public static uint Activate(string appUserModelId, string arguments)
    {
        var manager = (IDesktopSmokeApplicationActivationManager)
            new DesktopSmokeApplicationActivationManager();
        var result = manager.ActivateApplication(
            appUserModelId,
            arguments,
            0,
            out var processId);
        Marshal.ThrowExceptionForHR(result);
        return processId;
    }
}
"@
}

function Get-DesktopDevelopmentPackage {
    $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $package -or [string]::IsNullOrWhiteSpace($package.InstallLocation)) {
        throw "The Excalidraw Desktop development package is not registered correctly."
    }
    return $package
}

function Start-DesktopPackagedApp {
    param(
        [Parameter(Mandatory)] $Package,
        [string] $Arguments = ""
    )

    $applicationId = "$($Package.PackageFamilyName)!App"
    $processId = [DesktopSmokePackagedAppActivator]::Activate(
        $applicationId,
        $Arguments)
    return Get-Process -Id $processId
}

function Wait-DesktopWindowTitle {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [string] $ExpectedTitle,
        [Parameter(Mandatory)] [DateTime] $DeadlineUtc,
        [string] $FailurePrefix
    )

    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Excalidraw Desktop exited before reaching '$ExpectedTitle'."
        }
        if ($Process.MainWindowTitle -eq $ExpectedTitle) {
            return
        }
        if ($FailurePrefix -and $Process.MainWindowTitle.StartsWith(
                $FailurePrefix,
                [System.StringComparison]::Ordinal)) {
            throw $Process.MainWindowTitle
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for '$ExpectedTitle'. Last title: $($Process.MainWindowTitle)"
}

function Stop-DesktopTestProcess {
    param([System.Diagnostics.Process] $Process)

    if (-not $Process) {
        return
    }
    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force
            $null = $Process.WaitForExit(5000)
        }
    }
    catch [System.InvalidOperationException] {
        # The test-owned process already exited.
    }
}
