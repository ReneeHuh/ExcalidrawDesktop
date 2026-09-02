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
$generatedPaths = [System.Collections.Generic.List[string]]::new()

Add-Type -AssemblyName UIAutomationClient

Add-Type @"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class WorkspaceApplicationActivationManager {}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWorkspaceApplicationActivationManager
{
    int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

public static class WorkspacePackagedAppActivator
{
    public static uint Activate(string appUserModelId)
    {
        var manager = (IWorkspaceApplicationActivationManager)new WorkspaceApplicationActivationManager();
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
        throw "Close existing Excalidraw Desktop processes before running the workspace restore smoke test."
    }

    $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $package) {
        throw "The Excalidraw Desktop development package is not registered."
    }

    $installRoot = [System.IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\') + '\'
    $firstPath = Join-Path $package.InstallLocation "workspace-one.excalidraw"
    $secondPath = Join-Path $package.InstallLocation "workspace-two.excalidraw"
    $missingPath = Join-Path $package.InstallLocation "workspace-missing.excalidraw"
    $statePath = Join-Path $package.InstallLocation "workspace-smoke-state.json"
    $requestPath = Join-Path $package.InstallLocation "workspace-smoke.request"
    foreach ($path in @($firstPath, $secondPath, $statePath, $requestPath)) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith($installRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A workspace smoke path escaped the package install directory: $resolved"
        }
        $generatedPaths.Add($resolved)
    }

    $drawing = '{"type":"excalidraw","version":2,"elements":[],"appState":{},"files":{}}'
    [System.IO.File]::WriteAllText($firstPath, $drawing)
    [System.IO.File]::WriteAllText($secondPath, $drawing)
    $state = @{
        Version = 1
        Tabs = @(
            @{ Path = $firstPath; WasDirty = $false }
            @{ Path = $missingPath; WasDirty = $false }
            @{ Path = $secondPath; WasDirty = $false }
        )
        ActivePath = $secondPath
        RecentFiles = @($secondPath, $missingPath, $firstPath)
    } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($statePath, $state)
    [System.IO.File]::WriteAllText($requestPath, "run")

    $applicationId = "$($package.PackageFamilyName)!App"
    $processId = [WorkspacePackagedAppActivator]::Activate($applicationId)
    $startedProcess = Get-Process -Id $processId
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $expectedTitle = "workspace-two.excalidraw — Excalidraw Desktop"
    $workspaceRestored = $false

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "Excalidraw Desktop exited during workspace restoration."
        }

        if (-not $workspaceRestored -and
            $startedProcess.MainWindowTitle -eq $expectedTitle) {
            $restoredState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
            if ($restoredState.Version -eq 3 -and
                $restoredState.Windows.Count -eq 1 -and
                $restoredState.Windows[0].Tabs.Count -eq 2 -and
                $restoredState.RecentFiles.Count -eq 2 -and
                $restoredState.RecentFiles -notcontains $missingPath) {
                $workspaceRestored = $true
                [pscustomobject]@{
                    Result = "Passed"
                    ProcessId = $startedProcess.Id
                    RestoredTabs = 2
                    ActiveFile = "workspace-two.excalidraw"
                    MissingRecentFilesPruned = $true
                }
                return
            }
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for workspace restoration. Last title: $($startedProcess.MainWindowTitle)"
}
finally {
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

    if (-not $KeepRunning) {
        foreach ($path in $generatedPaths) {
            if ([System.IO.File]::Exists($path)) {
                [System.IO.File]::Delete($path)
            }
        }
    }
}
