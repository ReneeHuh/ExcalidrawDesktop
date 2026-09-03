[CmdletBinding()]
param(
    [ValidateRange(10, 180)]
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
        & $buildScript -Configuration Debug -SkipRestore -Deploy
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/deploy command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the document-safety smoke test."
    }

    $package = Get-DesktopDevelopmentPackage
    $installRoot = [System.IO.Path]::GetFullPath(
        $package.InstallLocation).TrimEnd('\') + '\'
    $requestPath = Join-Path $package.InstallLocation "document-safety-smoke.request"
    $statePath = Join-Path $package.InstallLocation "document-safety-smoke-state.json"
    foreach ($path in @($requestPath, $statePath)) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith(
                $installRoot,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A document-safety smoke path escaped the package install directory: $resolved"
        }
        if ([System.IO.File]::Exists($resolved)) {
            [System.IO.File]::Delete($resolved)
        }
    }

    [System.IO.File]::WriteAllText($requestPath, "run")
    $startedProcess = Start-DesktopPackagedApp -Package $package
    Wait-DesktopWindowTitle `
        -Process $startedProcess `
        -ExpectedTitle "Excalidraw Desktop — Document safety smoke passed" `
        -FailurePrefix "Excalidraw Desktop — Document safety smoke failed:" `
        -DeadlineUtc ([DateTime]::UtcNow.AddSeconds($TimeoutSeconds))

    [pscustomobject]@{
        Result = "Passed"
        ProcessId = $startedProcess.Id
        SaveIsolation = $true
        BridgeIsolation = $true
        ExternalModification = $true
        ExternalMoveOrRename = $true
        ExternalDeletion = $true
    }
}
finally {
    foreach ($path in @($requestPath, $statePath)) {
        if ($path -and [System.IO.File]::Exists($path)) {
            [System.IO.File]::Delete($path)
        }
    }
    if (-not $KeepRunning) {
        Stop-DesktopTestProcess -Process $startedProcess
    }
}
