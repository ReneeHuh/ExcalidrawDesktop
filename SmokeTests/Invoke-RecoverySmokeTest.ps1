[CmdletBinding()]
param(
    [ValidateRange(10, 120)]
    [int] $TimeoutSeconds = 45,

    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
$testRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $testRoot
$buildScript = Join-Path $repositoryRoot "tools\Build-Desktop.ps1"
$startedProcess = $null
$generatedFiles = [System.Collections.Generic.List[string]]::new()
$recoveryDirectory = $null

Add-Type @"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class RecoveryApplicationActivationManager {}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRecoveryApplicationActivationManager
{
    int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

public static class RecoveryPackagedAppActivator
{
    public static uint Activate(string appUserModelId)
    {
        var manager = (IRecoveryApplicationActivationManager)new RecoveryApplicationActivationManager();
        var result = manager.ActivateApplication(appUserModelId, "", 0, out var processId);
        Marshal.ThrowExceptionForHR(result);
        return processId;
    }
}
"@

function Wait-ForTitle {
    param(
        [System.Diagnostics.Process] $Process,
        [string] $ExpectedTitle,
        [DateTime] $DeadlineUtc
    )

    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Excalidraw Desktop exited before reaching '$ExpectedTitle'."
        }
        if ($Process.MainWindowTitle -eq $ExpectedTitle) {
            return
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for '$ExpectedTitle'. Last title: $($Process.MainWindowTitle)"
}

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Deploy
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/deploy command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the recovery smoke test."
    }

    $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $package) {
        throw "The Excalidraw Desktop development package is not registered."
    }

    $installRoot = [System.IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\') + '\'
    $statePath = Join-Path $package.InstallLocation "recovery-smoke-state.json"
    $snapshotRequestPath = Join-Path $package.InstallLocation "recovery-smoke.request"
    $restoreRequestPath = Join-Path $package.InstallLocation "recovery-restore.request"
    $recoveryDirectory = Join-Path $package.InstallLocation "recovery-smoke-state.recovery"
    foreach ($path in @($statePath, $snapshotRequestPath, $restoreRequestPath, $recoveryDirectory)) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith($installRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A recovery smoke path escaped the package install directory: $resolved"
        }
    }
    $generatedFiles.Add($statePath)
    $generatedFiles.Add($snapshotRequestPath)
    $generatedFiles.Add($restoreRequestPath)

    if ([System.IO.Directory]::Exists($recoveryDirectory)) {
        [System.IO.Directory]::Delete($recoveryDirectory, $true)
    }
    foreach ($path in $generatedFiles) {
        if ([System.IO.File]::Exists($path)) {
            [System.IO.File]::Delete($path)
        }
    }

    $applicationId = "$($package.PackageFamilyName)!App"
    [System.IO.File]::WriteAllText($snapshotRequestPath, "run")
    $processId = [RecoveryPackagedAppActivator]::Activate($applicationId)
    $startedProcess = Get-Process -Id $processId
    Wait-ForTitle -Process $startedProcess `
        -ExpectedTitle "Excalidraw Desktop — Recovery snapshot saved" `
        -DeadlineUtc ([DateTime]::UtcNow.AddSeconds($TimeoutSeconds))

    $savedState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($savedState.Tabs.Count -ne 2 -or
        @($savedState.Tabs | Where-Object { -not $_.WasDirty }).Count -ne 0) {
        throw "Both dirty tabs were not written to workspace recovery metadata."
    }
    foreach ($tab in $savedState.Tabs) {
        $snapshotPath = Join-Path $recoveryDirectory "$($tab.RecoveryId).excalidraw"
        if (-not [System.IO.File]::Exists($snapshotPath)) {
            throw "A dirty tab recovery snapshot was not written."
        }
    }

    Stop-Process -Id $startedProcess.Id -Force
    $null = $startedProcess.WaitForExit(5000)
    $startedProcess = $null

    [System.IO.File]::WriteAllText($restoreRequestPath, "run")
    $processId = [RecoveryPackagedAppActivator]::Activate($applicationId)
    $startedProcess = Get-Process -Id $processId
    Wait-ForTitle -Process $startedProcess `
        -ExpectedTitle "Excalidraw Desktop — Recovery smoke restored" `
        -DeadlineUtc ([DateTime]::UtcNow.AddSeconds($TimeoutSeconds))

    [pscustomobject]@{
        Result = "Passed"
        ProcessId = $startedProcess.Id
        ForcedTermination = $true
        DirtyUntitledTabsRestored = 2
        SnapshotValidatedByEditor = $true
    }
}
finally {
    if ($startedProcess) {
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

    foreach ($path in $generatedFiles) {
        if ([System.IO.File]::Exists($path)) {
            [System.IO.File]::Delete($path)
        }
    }
    if ($recoveryDirectory -and [System.IO.Directory]::Exists($recoveryDirectory)) {
        [System.IO.Directory]::Delete($recoveryDirectory, $true)
    }
}
