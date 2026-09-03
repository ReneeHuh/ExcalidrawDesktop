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
$startedProcess = $null
$requestPath = $null
$statePath = $null

Add-Type @"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class MultiWindowApplicationActivationManager {}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMultiWindowApplicationActivationManager
{
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        uint options,
        out uint processId);
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

public static class MultiWindowPackagedAppActivator
{
    public static uint Activate(string appUserModelId)
    {
        var manager = (IMultiWindowApplicationActivationManager)
            new MultiWindowApplicationActivationManager();
        var result = manager.ActivateApplication(appUserModelId, "", 0, out var processId);
        Marshal.ThrowExceptionForHR(result);
        return processId;
    }
}

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
        & $buildScript -Configuration Debug -SkipRestore -Deploy
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/deploy command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the multi-window smoke test."
    }

    $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $package -or [string]::IsNullOrWhiteSpace($package.InstallLocation)) {
        throw "The Excalidraw Desktop development package is not registered correctly."
    }

    $requestPath = Join-Path $package.InstallLocation "multi-window-smoke.request"
    $statePath = Join-Path $package.InstallLocation "multi-window-smoke-state.json"
    if ([System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    [System.IO.File]::WriteAllText($requestPath, "run")
    $applicationId = "$($package.PackageFamilyName)!App"
    $processId = [MultiWindowPackagedAppActivator]::Activate($applicationId)
    $startedProcess = Get-Process -Id $processId
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
                SaveIsolation = $true
                SaveAsIsolation = $true
                ExternalChangeIsolation = $true
                RecoveryIdentityPreserved = $true
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
