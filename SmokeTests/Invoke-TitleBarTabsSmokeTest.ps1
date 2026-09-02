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

Add-Type -AssemblyName UIAutomationClient

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

public static class TitleBarPackagedAppActivator
{
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr window);

    public static uint Activate(string appUserModelId)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        var result = manager.ActivateApplication(appUserModelId, "", 0, out var processId);
        Marshal.ThrowExceptionForHR(result);
        return processId;
    }
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
        & $buildScript -Configuration Debug -SkipRestore -Deploy
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/deploy command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the title-bar smoke test."
    }

    $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $package) {
        throw "The Excalidraw Desktop development package is not registered."
    }

    $applicationId = "$($package.PackageFamilyName)!App"
    $requestPath = Join-Path $package.InstallLocation "titlebar-smoke.request"
    $statePath = Join-Path $package.InstallLocation "titlebar-smoke-state.json"
    if ([System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    [System.IO.File]::WriteAllText($requestPath, "run")
    $processId = [TitleBarPackagedAppActivator]::Activate($applicationId)
    $startedProcess = Get-Process -Id $processId
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "Excalidraw Desktop exited during the title-bar smoke test."
        }

        if ($startedProcess.MainWindowTitle -eq "Excalidraw Desktop — Title bar smoke failed") {
            throw "The packaged title-bar layout or tab interaction check failed."
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
                if ($tabItem.Current.Name -match "(saved|unsaved changes|new drawing)") {
                    $namedDrawingTab = $true
                    break
                }
            }
            if (-not $namedDrawingTab) {
                throw "No drawing tab exposed its document state in its accessible name."
            }

            $dpi = [TitleBarPackagedAppActivator]::GetDpiForWindow(
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

    throw "Timed out waiting for the packaged title-bar check. Last title: $($startedProcess.MainWindowTitle)"
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
