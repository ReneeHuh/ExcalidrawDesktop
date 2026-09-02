[CmdletBinding()]
param(
    [ValidateRange(5, 120)]
    [int] $TimeoutSeconds = 30,

    [switch] $SkipBuild,

    [switch] $KeepRunning
)

$ErrorActionPreference = "Stop"
$testRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $testRoot
$buildScript = Join-Path $repositoryRoot "tools\Build-Desktop.ps1"
$startedProcess = $null
$requestPath = $null
$statePath = $null

Add-Type @"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class ApplicationActivationManager {}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager
{
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        uint options,
        out uint processId);
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

public static class SuspensionPackagedAppActivator
{
    public static uint Activate(string appUserModelId)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        var result = manager.ActivateApplication(appUserModelId, "", 0, out var processId);
        Marshal.ThrowExceptionForHR(result);
        return processId;
    }
}
"@

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Deploy
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/deploy command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the suspension smoke test."
    }

    $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $package) {
        throw "The Excalidraw Desktop development package is not registered."
    }

    $applicationId = "$($package.PackageFamilyName)!App"
    $requestPath = Join-Path $package.InstallLocation "suspension-smoke.request"
    $statePath = Join-Path $package.InstallLocation "suspension-smoke-state.json"
    if ([System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    [System.IO.File]::WriteAllText($requestPath, "run")
    $processId = [SuspensionPackagedAppActivator]::Activate($applicationId)
    $startedProcess = Get-Process -Id $processId
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "Excalidraw Desktop exited during the suspension smoke test."
        }
        if ($startedProcess.MainWindowTitle -like "Excalidraw Desktop — Suspension smoke failed*") {
            throw "The packaged clean-tab lifecycle check failed. $($startedProcess.MainWindowTitle)"
        }
        if ($startedProcess.MainWindowTitle -eq "Excalidraw Desktop — Suspension smoke passed") {
            [pscustomobject]@{
                Result = "Passed"
                ProcessId = $startedProcess.Id
                CleanInactiveTabSuspended = $true
                SelectedTabResumed = $true
                CleanInactiveTabUnloaded = $true
                SelectedTabRecreated = $true
                UniqueOriginPreserved = $true
            }
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for the packaged suspension check. Last title: $($startedProcess.MainWindowTitle)"
}
finally {
    foreach ($path in @($requestPath, $statePath)) {
        if ($path -and [System.IO.File]::Exists($path)) {
            [System.IO.File]::Delete($path)
        }
    }
    if ($startedProcess -and -not $KeepRunning) {
        try {
            if (-not $startedProcess.HasExited) {
                Stop-Process -Id $startedProcess.Id -Force
                $null = $startedProcess.WaitForExit(5000)
            }
        }
        catch [System.InvalidOperationException] {
            # The test-owned process already exited.
        }
    }
}
