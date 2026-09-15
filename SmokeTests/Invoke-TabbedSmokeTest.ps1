[CmdletBinding()]
param(
    [ValidateRange(5, 120)]
    [int] $TimeoutSeconds = 60,

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

    $existing = @(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the tab smoke test."
    }

    $package = Get-DesktopTestApplication
    $package.DataRoot = Join-Path ([IO.Path]::GetTempPath()) ("ExcalidrawSmoke-tabbed-" + [Guid]::NewGuid().ToString("N"))
    $requestPath = Join-Path $package.InstallLocation "tab-smoke.request"
    $statePath = Join-Path $package.InstallLocation "tab-smoke-state.json"
    if ([System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    [System.IO.File]::WriteAllText($requestPath, "run")
    $startedProcess = Start-DesktopTestApplication `
        -Application $package `
        -Arguments "--tab-smoke"
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "Excalidraw Desktop exited during the tab smoke test."
        }

        if ($startedProcess.MainWindowTitle.StartsWith(
                "Excalidraw Desktop — Tab smoke failed",
                [System.StringComparison]::Ordinal)) {
            throw "The unpackaged tab isolation check failed. $($startedProcess.MainWindowTitle)"
        }

        if ($startedProcess.MainWindowTitle -eq "Excalidraw Desktop — Tab smoke passed") {
            [pscustomobject]@{
                Result = "Passed"
                ProcessId = $startedProcess.Id
                TabCount = 2
                UniqueOrigins = 2
                LocalStorageIsolated = $true
                IndexedDbIsolated = $true
                SceneStateIsolated = $true
                ViewportAndZoomIsolated = $true
                SelectionIsolated = $true
                UndoHistoryIsolated = $true
                SavedDrawingRestoredOnRetry = $true
                RecoverySnapshotRestoredOnRetry = $true
                UnavailableRecoveryBlocksRetry = $true
                PendingCloseCancelledOnEditorFailure = $true
                BackgroundChangesTracked = $true
                BackgroundRecoveryRestored = $true
                BackgroundSaveClearedDirtyState = $true
                ViewportChangesIgnored = $true
                DeletedShapeSaveClearedDirtyState = $true
                SilentEditorCloseTimedOut = $true
                LateCloseAcknowledgementIgnored = $true
                CloseRetryAfterTimeoutSucceeded = $true
                RecoveryRetriedAfterWriteFailure = $true
                SaveCancellationReleasedEditor = $true
                SaveRetryAfterCancellationSucceeded = $true
                EditorFailureDuringPngExportHandled = $true
                RecoveryExternalChangesProtected = $true
                LegacyRecoveryRequiresConflictResolution = $true
            }
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for the unpackaged tab isolation check. Last title: $($startedProcess.MainWindowTitle)"
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
