[CmdletBinding()]
param(
    [ValidateRange(5, 120)]
    [int] $TimeoutSeconds = 45,

    [switch] $SkipBuild,

    [switch] $Dirty
)

$ErrorActionPreference = "Stop"
$testRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $testRoot
$buildScript = Join-Path $repositoryRoot "tools\Build-Desktop.ps1"
. (Join-Path $testRoot "SmokeTestCommon.ps1")
$startedProcess = $null
$requestPath = $null
$statePath = $null
$readyPath = $null
$discardDecisions = 0
$clickedDiscardButtons = [System.Collections.Generic.HashSet[string]]::new()

Add-Type -AssemblyName UIAutomationClient

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Publish
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/publish command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the multi-window Exit smoke test."
    }

    $package = Get-DesktopTestApplication

    $installRoot = [System.IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\') + '\'
    $requestPath = Join-Path $package.InstallLocation $(if ($Dirty) {
        "multi-window-dirty-exit-smoke.request"
    } else {
        "multi-window-exit-smoke.request"
    })
    $statePath = Join-Path $package.InstallLocation "multi-window-exit-smoke-state.json"
    $readyPath = Join-Path $package.InstallLocation "multi-window-exit-smoke.ready"
    foreach ($path in @($requestPath, $statePath, $readyPath)) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith($installRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A multi-window Exit smoke path escaped the application directory: $resolved"
        }
        if ([System.IO.File]::Exists($resolved)) {
            [System.IO.File]::Delete($resolved)
        }
    }

    [System.IO.File]::WriteAllText($requestPath, "run")
    $startedProcess = Start-DesktopTestApplication -Application $package
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $readyObserved = $false

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        if ([System.IO.File]::Exists($readyPath)) {
            $readyObserved = $true
        }

        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            if (-not $readyObserved) {
                throw "Excalidraw Desktop exited before the two-window Exit setup completed."
            }
            if (-not [System.IO.File]::Exists($statePath)) {
                throw "The aggregate workspace was not persisted before Exit."
            }

            $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
            if ($state.Version -ne 3 -or $state.Windows.Count -ne 2) {
                throw "Exit did not retain the two-window version-3 workspace."
            }
            $dirtyTabs = @($state.Windows | ForEach-Object { $_.Tabs } |
                Where-Object { $_.WasDirty })
            if ($Dirty -and $discardDecisions -ne 2) {
                $readyTrace = Get-Content -LiteralPath $readyPath -Raw
                throw "The dirty Exit flow resolved $discardDecisions of 2 window dialogs. Trace: $readyTrace"
            }
            if ($dirtyTabs.Count -ne 0) {
                $dirtySummary = $dirtyTabs | ForEach-Object {
                    "$($_.DisplayName):$($_.RecoveryId)"
                }
                $readyTrace = Get-Content -LiteralPath $readyPath -Raw
                throw "Discarded drawings remained dirty in the persisted workspace: $($dirtySummary -join ', '). Trace: $readyTrace"
            }

            [pscustomobject]@{
                Result = "Passed"
                ProcessId = $startedProcess.Id
                CleanWindowsClosed = 2
                ProcessExited = $true
                AggregateWorkspacePersisted = $true
                DirtyWindowsDiscarded = if ($Dirty) { 2 } else { 0 }
            }
            return
        }

        if ($Dirty -and $readyObserved -and $discardDecisions -lt 2) {
            $processCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
                $startedProcess.Id)
            $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
                "SecondaryButton")
            $condition = [System.Windows.Automation.AndCondition]::new(
                $processCondition,
                $nameCondition)
            $button = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $condition)
            if ($button -and $button.Current.IsEnabled) {
                $runtimeId = $button.GetRuntimeId() -join "."
                if ($clickedDiscardButtons.Add($runtimeId)) {
                    $invoke = $button.GetCurrentPattern(
                        [System.Windows.Automation.InvokePattern]::Pattern)
                    $invoke.Invoke()
                    $discardDecisions++
                    Start-Sleep -Milliseconds 500
                }
            }
        }

        if ($startedProcess.MainWindowTitle -like "Excalidraw Desktop — Multi-window Exit smoke failed:*") {
            throw $startedProcess.MainWindowTitle
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for coordinated multi-window Exit."
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
    foreach ($path in @($requestPath, $statePath, $readyPath)) {
        if ($path -and [System.IO.File]::Exists($path)) {
            [System.IO.File]::Delete($path)
        }
    }
}
