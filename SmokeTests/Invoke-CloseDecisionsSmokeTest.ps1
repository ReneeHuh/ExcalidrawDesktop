[CmdletBinding()]
param(
    [ValidateRange(15, 180)]
    [int] $TimeoutSeconds = 90,

    [switch] $SkipBuild,

    [switch] $KeepRunning
)

$ErrorActionPreference = "Stop"
$testRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $testRoot
$buildScript = Join-Path $repositoryRoot "tools\Build-Desktop.ps1"
. (Join-Path $testRoot "SmokeTestCommon.ps1")
Add-Type -AssemblyName UIAutomationClient

$startedProcess = $null
$requestPath = $null
$statePath = $null
$resultPath = $null

function Invoke-DialogButton {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [string] $ButtonAutomationId,
        [Parameter(Mandatory)] [DateTime] $DeadlineUtc
    )

    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $Process.Id)
    $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $buttonIdCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $ButtonAutomationId)

    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Excalidraw Desktop exited while waiting for '$ButtonAutomationId'."
        }
        if ($Process.MainWindowTitle.StartsWith(
                "Excalidraw Desktop — Close decisions smoke failed:",
                [System.StringComparison]::Ordinal)) {
            throw $Process.MainWindowTitle
        }

        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.AndCondition]::new(
                $processCondition,
                $windowCondition))
        $button = $null
        foreach ($window in $windows) {
            if ($window.Current.NativeWindowHandle -ne 0) {
                continue
            }
            $candidate = $window.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $buttonIdCondition)
            if ($candidate -and $candidate.Current.IsEnabled) {
                $button = $candidate
                break
            }
        }
        if ($button -and $button.Current.IsEnabled) {
            $invoke = $button.GetCurrentPattern(
                [System.Windows.Automation.InvokePattern]::Pattern)
            $invoke.Invoke()
            Start-Sleep -Milliseconds 500
            return
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for the '$ButtonAutomationId' close-decision button. Last title: $($Process.MainWindowTitle)"
}

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Deploy
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/deploy command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the close-decisions smoke test."
    }

    $package = Get-DesktopDevelopmentPackage
    $installRoot = [System.IO.Path]::GetFullPath(
        $package.InstallLocation).TrimEnd('\') + '\'
    $requestPath = Join-Path $package.InstallLocation "close-decisions-smoke.request"
    $statePath = Join-Path $package.InstallLocation "close-decisions-smoke-state.json"
    $resultPath = Join-Path $package.InstallLocation "close-decisions-smoke.result"
    foreach ($path in @($requestPath, $statePath, $resultPath)) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith(
                $installRoot,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A close-decisions smoke path escaped the package install directory: $resolved"
        }
        if ([System.IO.File]::Exists($resolved)) {
            [System.IO.File]::Delete($resolved)
        }
    }

    [System.IO.File]::WriteAllText($requestPath, "run")
    $startedProcess = Start-DesktopPackagedApp -Package $package
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    Invoke-DialogButton -Process $startedProcess -ButtonAutomationId "CloseButton" -DeadlineUtc $deadlineUtc
    Invoke-DialogButton -Process $startedProcess -ButtonAutomationId "SecondaryButton" -DeadlineUtc $deadlineUtc
    Invoke-DialogButton -Process $startedProcess -ButtonAutomationId "PrimaryButton" -DeadlineUtc $deadlineUtc
    Invoke-DialogButton -Process $startedProcess -ButtonAutomationId "CloseButton" -DeadlineUtc $deadlineUtc
    Invoke-DialogButton -Process $startedProcess -ButtonAutomationId "ReviewTabsButton" -DeadlineUtc $deadlineUtc
    Invoke-DialogButton -Process $startedProcess -ButtonAutomationId "PrimaryButton" -DeadlineUtc $deadlineUtc
    while ([DateTime]::UtcNow -lt $deadlineUtc -and
        -not [System.IO.File]::Exists($resultPath)) {
        Start-Sleep -Milliseconds 100
    }
    if (-not [System.IO.File]::Exists($resultPath)) {
        throw "The app did not write a close-decisions result. Last title: $($startedProcess.MainWindowTitle)"
    }
    $result = [System.IO.File]::ReadAllText($resultPath)
    if ($result -ne "passed") {
        throw "The close-decisions smoke test failed in the app: $result"
    }

    [pscustomobject]@{
        Result = "Passed"
        ProcessId = $startedProcess.Id
        CancelPreservedDirtyDrawing = $true
        DiscardClosedDirtyDrawing = $true
        SaveWroteOwningFileAndClosed = $true
        WindowCancelPreservedAllDirtyDrawings = $true
        ReviewSelectedFirstDirtyDrawing = $true
        SaveAllWroteEveryOwningFile = $true
    }
}
finally {
    foreach ($path in @($requestPath, $statePath, $resultPath)) {
        if ($path -and [System.IO.File]::Exists($path)) {
            [System.IO.File]::Delete($path)
        }
    }
    if (-not $KeepRunning) {
        Stop-DesktopTestProcess -Process $startedProcess
    }
}
