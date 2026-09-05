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
. (Join-Path $testRoot "SmokeTestCommon.ps1")
$startedProcess = $null
$generatedPaths = [System.Collections.Generic.List[string]]::new()

Add-Type -AssemblyName UIAutomationClient

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Publish
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/publish command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the workspace restore smoke test."
    }

    $package = Get-DesktopTestApplication

    $installRoot = [System.IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\') + '\'
    $firstPath = Join-Path $package.InstallLocation "workspace-one.excalidraw"
    $secondPath = Join-Path $package.InstallLocation "workspace-two.excalidraw"
    $missingPath = Join-Path $package.InstallLocation "workspace-missing.excalidraw"
    $statePath = Join-Path $package.InstallLocation "workspace-smoke-state.json"
    $requestPath = Join-Path $package.InstallLocation "workspace-smoke.request"
    foreach ($path in @($firstPath, $secondPath, $statePath, $requestPath)) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith($installRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A workspace smoke path escaped the application directory: $resolved"
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

    $startedProcess = Start-DesktopTestApplication -Application $package
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
