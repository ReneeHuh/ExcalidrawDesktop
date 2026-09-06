[CmdletBinding()]
param(
    [ValidateRange(5, 120)]
    [int] $TimeoutSeconds = 45,

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

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class MultiWindowProcessWindows
{
    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr windowHandle, System.Text.StringBuilder text, int count);

    public static string[] GetTitles(uint processId)
    {
        var titles = new System.Collections.Generic.List<string>();
        EnumWindows((windowHandle, parameter) =>
        {
            GetWindowThreadProcessId(windowHandle, out var ownerProcessId);
            if (ownerProcessId == processId)
            {
                var text = new System.Text.StringBuilder(512);
                if (GetWindowText(windowHandle, text, text.Capacity) > 0)
                {
                    titles.Add(text.ToString());
                }
            }
            return true;
        }, IntPtr.Zero);
        return titles.ToArray();
    }
}
"@

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Publish
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/publish command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the multi-window smoke test."
    }

    $package = Get-DesktopTestApplication

    $requestPath = Join-Path $package.InstallLocation "multi-window-smoke.request"
    $statePath = Join-Path $package.InstallLocation "multi-window-smoke-state.json"
    if ([System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    [System.IO.File]::WriteAllText($requestPath, "run")
    $startedProcess = Start-DesktopTestApplication -Application $package
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "Excalidraw Desktop exited during the multi-window smoke test."
        }

        $windowTitles = [MultiWindowProcessWindows]::GetTitles($startedProcess.Id)
        $failureTitle = $windowTitles |
            Where-Object { $_ -like "Excalidraw Desktop — Multi-window smoke failed:*" } |
            Select-Object -First 1
        if ($failureTitle) {
            throw $failureTitle
        }

        if ($windowTitles -contains "Excalidraw Desktop — Multi-window smoke passed") {
            [pscustomobject]@{
                Result = "Passed"
                ProcessId = $startedProcess.Id
                IndependentWindows = $true
                LiveSessionMoved = $true
                WebViewIdentityPreserved = $true
                OriginPreserved = $true
                WindowHandlersRebound = $true
                EmptyDestinationClosed = $true
                UnsafeMoveRejected = $true
                DirtyStatePreserved = $true
                WatcherRebound = $true
                RepeatedMoveCleanup = $true
                TearOutPreparationHiddenFromSwitchers = $true
                CancelledTearOutCleanedUp = $true
                TornOutWindowRevealed = $true
                UnavailableTabTearOutWindowValid = $true
                RejectedTearOutWindowLifetimePreserved = $true
                SaveIsolation = $true
                SaveAsIsolation = $true
                ExternalChangeIsolation = $true
                RecoveryIdentityPreserved = $true
                InitializationMoveRejected = $true
                ResumeMoveRejected = $true
                UnloadedMoveRejected = $true
                WebViewFailureMoveRejected = $true
                WebViewFailureRecovered = $true
            }
            return
        }

        Start-Sleep -Milliseconds 250
    }

    $lastTitles = [MultiWindowProcessWindows]::GetTitles($startedProcess.Id) -join "; "
    throw "Timed out waiting for multi-window validation. Last window titles: $lastTitles"
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
