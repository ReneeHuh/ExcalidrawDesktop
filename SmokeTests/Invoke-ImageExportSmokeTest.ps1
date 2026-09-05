[CmdletBinding()]
param(
    [ValidateRange(10, 120)]
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
$generatedPaths = [System.Collections.Generic.List[string]]::new()

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Publish
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/publish command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the image export smoke test."
    }

    $package = Get-DesktopTestApplication

    $installRoot = [System.IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\') + '\'
    $requestPath = Join-Path $package.InstallLocation "image-export-smoke.request"
    $statePath = Join-Path $package.InstallLocation "image-export-smoke-state.json"
    $scenePath = Join-Path $package.InstallLocation "image-export-smoke.excalidraw"
    $outputPath = Join-Path $package.InstallLocation "image-export-smoke.png"
    foreach ($path in @($requestPath, $statePath, $scenePath, $outputPath)) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith(
            $installRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "An image-export smoke path escaped the application directory: $resolved"
        }
        $generatedPaths.Add($resolved)
        if ([System.IO.File]::Exists($resolved)) {
            [System.IO.File]::Delete($resolved)
        }
    }

    [System.IO.File]::WriteAllText($requestPath, "run")
    $startedProcess = Start-DesktopTestApplication -Application $package
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $startedProcess.Refresh()
        if ($startedProcess.HasExited) {
            throw "Excalidraw Desktop exited during the image export smoke test."
        }
        if ($startedProcess.MainWindowTitle -like
            "Excalidraw Desktop — Image export smoke failed:*") {
            throw $startedProcess.MainWindowTitle
        }
        if ($startedProcess.MainWindowTitle -eq
            "Excalidraw Desktop — Image export smoke passed") {
            if (-not [System.IO.File]::Exists($outputPath)) {
                throw "The unpackaged image export did not create a PNG."
            }
            $bytes = [System.IO.File]::ReadAllBytes($outputPath)
            $signature = [byte[]] @(137, 80, 78, 71, 13, 10, 26, 10)
            if ($bytes.Length -lt 24 -or
                ($bytes[0..7] -join ',') -ne ($signature -join ',')) {
                throw "The unpackaged image export is not a valid PNG stream."
            }
            $width = [uint32] (
                $bytes[16] * 16777216 +
                $bytes[17] * 65536 +
                $bytes[18] * 256 +
                $bytes[19])
            $height = [uint32] (
                $bytes[20] * 16777216 +
                $bytes[21] * 65536 +
                $bytes[22] * 256 +
                $bytes[23])
            if ($width -lt 14000 -or $height -lt 3800) {
                throw "The PNG dimensions $($width)x$height do not include the full off-screen scene."
            }

            [pscustomobject]@{
                Result = "Passed"
                ProcessId = $startedProcess.Id
                PngBytes = $bytes.Length
                Width = $width
                Height = $height
                FullSceneBounds = $true
                EmbeddedImageInput = $true
                WebViewBinaryTransfer = $true
                TransactionalNativeWrite = $true
            }
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for unpackaged image export. Last title: $($startedProcess.MainWindowTitle)"
}
finally {
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
    foreach ($path in $generatedPaths) {
        if ([System.IO.File]::Exists($path)) {
            [System.IO.File]::Delete($path)
        }
    }
}
