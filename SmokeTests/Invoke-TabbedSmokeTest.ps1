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

public static class PackagedAppActivator
{
    public static uint Activate(string appUserModelId, string arguments)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        var result = manager.ActivateApplication(appUserModelId, arguments, 0, out var processId);
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

    $existing = @(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the tab smoke test."
    }

    $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $package) {
        throw "The Excalidraw Desktop development package is not registered."
    }

    $applicationId = "$($package.PackageFamilyName)!App"
    $requestPath = Join-Path $package.InstallLocation "tab-smoke.request"
    $statePath = Join-Path $package.InstallLocation "tab-smoke-state.json"
    if ([System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    [System.IO.File]::WriteAllText($requestPath, "run")
    $processId = [PackagedAppActivator]::Activate($applicationId, "--tab-smoke")
    $startedProcess = Get-Process -Id $processId
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "Excalidraw Desktop exited during the tab smoke test."
        }

        if ($startedProcess.MainWindowTitle -eq "Excalidraw Desktop — Tab smoke failed") {
            throw "The packaged tab storage-isolation check failed."
        }

        if ($startedProcess.MainWindowTitle -eq "Excalidraw Desktop — Tab smoke passed") {
            [pscustomobject]@{
                Result = "Passed"
                ProcessId = $startedProcess.Id
                TabCount = 2
                UniqueOrigins = 2
                LocalStorageIsolated = $true
            }
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for the packaged tab isolation check. Last title: $($startedProcess.MainWindowTitle)"
}
finally {
    if ($requestPath -and [System.IO.File]::Exists($requestPath)) {
        [System.IO.File]::Delete($requestPath)
    }
    if ($statePath -and [System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
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
