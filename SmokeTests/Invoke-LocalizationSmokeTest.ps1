[CmdletBinding()]
param(
    [ValidateSet("de-DE", "ar-SA")]
    [string[]] $Languages = @("de-DE", "ar-SA"),

    [ValidateRange(10, 120)]
    [int] $TimeoutSeconds = 45,

    [switch] $SkipBuild,

    [switch] $Restore
)

$ErrorActionPreference = "Stop"
$testRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $testRoot
$buildScript = Join-Path $repositoryRoot "tools\Build-Desktop.ps1"
$nativeRoot = Join-Path $repositoryRoot "ExcalidrawDesktop.App"
$expectedBuildRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $nativeRoot "bin\x64\Debug"))
$startedProcess = $null
$requestPath = $null
$resultPath = $null
$crashPath = $null
$statePath = $null
$results = @()

if ($SkipBuild -and $Restore) {
    throw "-Restore cannot be combined with -SkipBuild."
}

Add-Type @"
using System;
using System.Runtime.InteropServices;

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class LocalizationApplicationActivationManager {}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ILocalizationApplicationActivationManager
{
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        uint options,
        out uint processId);
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

public static class LocalizationPackagedAppActivator
{
    public static uint Activate(string appUserModelId)
    {
        var manager = (ILocalizationApplicationActivationManager)
            new LocalizationApplicationActivationManager();
        var result = manager.ActivateApplication(appUserModelId, "", 0, out var processId);
        Marshal.ThrowExceptionForHR(result);
        return processId;
    }
}
"@

try {
    if (-not $SkipBuild) {
        $buildArguments = @{
            Configuration = "Debug"
            Deploy = $true
        }
        if (-not $Restore) {
            $buildArguments.SkipRestore = $true
        }
        & $buildScript @buildArguments
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/deploy command failed with exit code $LASTEXITCODE."
        }
    }

    $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $package) {
        throw "The Excalidraw Desktop development package is not registered."
    }

    $installLocation = [System.IO.Path]::GetFullPath($package.InstallLocation)
    if (-not $installLocation.StartsWith(
        $expectedBuildRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The registered package points outside this build tree: $installLocation"
    }

    $existing = @(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the localization smoke test."
    }

    $applicationId = "$($package.PackageFamilyName)!App"
    $requestPath = Join-Path $installLocation "localization-smoke.request"
    $resultPath = Join-Path $installLocation "localization-smoke-result.json"
    $crashPath = Join-Path $installLocation "localization-smoke-crash.txt"
    $statePath = Join-Path $installLocation "localization-smoke-state.json"

    foreach ($language in $Languages) {
        foreach ($path in @($requestPath, $resultPath, $crashPath, $statePath)) {
            if ([System.IO.File]::Exists($path)) {
                [System.IO.File]::Delete($path)
            }
        }

        [System.IO.File]::WriteAllText($requestPath, $language)
        $processId = [LocalizationPackagedAppActivator]::Activate($applicationId)
        $startedProcess = Get-Process -Id $processId
        $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $passedTitle = "Excalidraw Desktop — Localization smoke passed: $language"
        $failedTitle = "Excalidraw Desktop — Localization smoke failed: $language"

        while ([DateTime]::UtcNow -lt $deadlineUtc) {
            $startedProcess.Refresh()
            if ($startedProcess.HasExited) {
                $crash = if ([System.IO.File]::Exists($crashPath)) {
                    Get-Content -LiteralPath $crashPath -Raw
                } else {
                    "No managed crash diagnostic was written."
                }
                throw "Excalidraw Desktop exited during the $language localization smoke test.`n$crash"
            }
            if ($startedProcess.MainWindowTitle -eq $failedTitle) {
                $failureResult = if ([System.IO.File]::Exists($resultPath)) {
                    Get-Content -LiteralPath $resultPath -Raw
                } else {
                    "No localization result was written."
                }
                throw "The $language localization smoke test failed: $failureResult"
            }
            if ($startedProcess.MainWindowTitle -eq $passedTitle) {
                break
            }
            Start-Sleep -Milliseconds 250
        }

        if ($startedProcess.MainWindowTitle -ne $passedTitle) {
            throw "Timed out waiting for the $language localization smoke test. Last title: $($startedProcess.MainWindowTitle)"
        }
        if (-not [System.IO.File]::Exists($resultPath)) {
            throw "The $language localization smoke test did not write its result."
        }

        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        $expectedDirection = if ($language -eq "ar-SA") { "rtl" } else { "ltr" }
        if ($result.Result -ne "Passed" -or
            $result.RequestedLanguage -ne $language -or
            $result.NativeLanguage -ne $language -or
            $result.ExcalidrawLanguage -ne $language -or
            $result.Direction -ne $expectedDirection -or
            -not $result.NativeResourcesApplied -or
            -not $result.NewEditorValidated -or
            -not $result.RetriedEditorValidated) {
            throw "The $language localization result was incomplete: $($result | ConvertTo-Json -Compress)"
        }

        $results += $result
        Stop-Process -Id $startedProcess.Id -Force
        $null = $startedProcess.WaitForExit(5000)
        $startedProcess = $null
    }

    $results
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
    foreach ($path in @($requestPath, $resultPath, $crashPath, $statePath)) {
        if ($path -and [System.IO.File]::Exists($path)) {
            [System.IO.File]::Delete($path)
        }
    }
}
