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
$requestPath = $null
$statePath = $null

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Publish
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/publish command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the suspension smoke test."
    }

    $package = Get-DesktopTestApplication
    $requestPath = Join-Path $package.InstallLocation "suspension-smoke.request"
    $statePath = Join-Path $package.InstallLocation "suspension-smoke-state.json"
    if ([System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    [System.IO.File]::WriteAllText($requestPath, "run")
    $startedProcess = Start-DesktopTestApplication -Application $package
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "Excalidraw Desktop exited during the suspension smoke test."
        }
        if ($startedProcess.MainWindowTitle -like "Excalidraw Desktop — Suspension smoke failed*") {
            throw "The unpackaged clean-tab lifecycle check failed. $($startedProcess.MainWindowTitle)"
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
                LargeScenePreserved = $true
                EmbeddedImagePreserved = $true
                UnloadFailureKeptEditorLive = $true
                RepeatedUnloadRecreateCycles = 3
            }
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for the unpackaged suspension check. Last title: $($startedProcess.MainWindowTitle)"
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
