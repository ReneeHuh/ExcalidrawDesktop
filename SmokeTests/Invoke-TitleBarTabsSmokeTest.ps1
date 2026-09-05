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

Add-Type -AssemblyName UIAutomationClient

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class TitleBarDesktopTestNativeMethods
{
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr window);
}
"@

function Get-AutomationElementById {
    param(
        [System.Windows.Automation.AutomationElement] $Root,
        [string] $AutomationId
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Publish
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/publish command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the title-bar smoke test."
    }

    $package = Get-DesktopTestApplication
    $requestPath = Join-Path $package.InstallLocation "titlebar-smoke.request"
    $statePath = Join-Path $package.InstallLocation "titlebar-smoke-state.json"
    $errorPath = Join-Path $package.InstallLocation "titlebar-smoke-error.txt"
    if ([System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    if ([System.IO.File]::Exists($errorPath)) {
        [System.IO.File]::Delete($errorPath)
    }
    [System.IO.File]::WriteAllText($requestPath, "run")
    $startedProcess = Start-DesktopTestApplication -Application $package
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "Excalidraw Desktop exited during the title-bar smoke test."
        }

        if ($startedProcess.MainWindowTitle -eq "Excalidraw Desktop — Title bar smoke failed") {
            $details = if ([System.IO.File]::Exists($errorPath)) {
                [System.IO.File]::ReadAllText($errorPath)
            } else {
                "No in-app diagnostic was written."
            }
            throw "The unpackaged title-bar layout or tab interaction check failed. $details"
        }

        if ($startedProcess.MainWindowTitle -eq "Excalidraw Desktop — Title bar smoke passed") {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle(
                $startedProcess.MainWindowHandle)
            $requiredAutomationIds = @(
                "DocumentTabs",
                "ApplicationMenu",
                "FileMenu",
                "SettingsButton",
                "ExcalidrawEditorWebView",
                "Minimize",
                "Maximize",
                "Close")
            $missingIds = @($requiredAutomationIds | Where-Object {
                -not (Get-AutomationElementById -Root $root -AutomationId $_)
            })
            if ($missingIds.Count -gt 0) {
                throw "Required automation controls were missing: $($missingIds -join ', ')"
            }

            $tabItems = $root.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::TabItem))
            $namedDrawingTab = $false
            foreach ($tabItem in $tabItems) {
                if (-not [string]::IsNullOrWhiteSpace($tabItem.Current.Name)) {
                    $namedDrawingTab = $true
                    break
                }
            }
            if (-not $namedDrawingTab) {
                throw "No drawing tab exposed its document state in its accessible name."
            }

            $dpi = [TitleBarDesktopTestNativeMethods]::GetDpiForWindow(
                $startedProcess.MainWindowHandle)
            [pscustomobject]@{
                Result = "Passed"
                ProcessId = $startedProcess.Id
                ContentExtended = $true
                InsetsDpiAdjusted = $true
                CurrentDpi = $dpi
                CurrentScalePercent = [Math]::Round($dpi / 96 * 100)
                DragRegionPresent = $true
                TabInteractions = "Passed"
                KeyboardAccelerators = "Passed"
                LightAndDarkThemes = "Passed"
                AutomationNames = "Passed"
                NativeStatusBar = "Passed"
                CaptionButtonAutomation = "Passed"
                FailureActions = "Passed"
                FailureRetry = "Passed"
                ClosedTabResourceCleanup = "Passed"
                RecoverySnapshotCleanup = "Passed"
            }
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for the unpackaged title-bar check. Last title: $($startedProcess.MainWindowTitle)"
}
finally {
    if ($requestPath -and [System.IO.File]::Exists($requestPath)) {
        [System.IO.File]::Delete($requestPath)
    }
    if ($statePath -and [System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    if ($errorPath -and [System.IO.File]::Exists($errorPath)) {
        [System.IO.File]::Delete($errorPath)
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
