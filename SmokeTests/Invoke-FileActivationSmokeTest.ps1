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
$generatedPaths = [System.Collections.Generic.List[string]]::new()

Add-Type -AssemblyName UIAutomationClient

function Wait-ForTitle {
    param(
        [System.Diagnostics.Process] $Process,
        [string] $Prefix
    )

    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Excalidraw Desktop exited during file activation."
        }
        if ($Process.MainWindowTitle.StartsWith(
            $Prefix,
            [System.StringComparison]::Ordinal)) {
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Timed out waiting for '$Prefix'. Last title: $($Process.MainWindowTitle)"
}

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Deploy
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/deploy command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the file activation smoke test."
    }

    $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if (-not $package) {
        throw "The Excalidraw Desktop development package is not registered."
    }

    $manifest = Get-AppxPackageManifest $package
    $association = $manifest.Package.Applications.Application.Extensions.Extension |
        Where-Object { $_.Category -eq "windows.fileTypeAssociation" } |
        Select-Object -First 1
    if (-not $association -or
        $association.FileTypeAssociation.SupportedFileTypes.FileType -notcontains ".excalidraw") {
        throw "The registered package does not declare the .excalidraw association."
    }

    $installRoot = [System.IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\') + '\'
    $firstPath = Join-Path $package.InstallLocation "activation-one.excalidraw"
    $secondPath = Join-Path $package.InstallLocation "activation-two.excalidraw"
    $requestPath = Join-Path $package.InstallLocation "file-activation-smoke.request"
    $statePath = Join-Path $package.InstallLocation "file-activation-smoke-state.json"
    foreach ($path in @($firstPath, $secondPath, $requestPath, $statePath)) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith($installRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A file activation smoke path escaped the package directory: $resolved"
        }
        $generatedPaths.Add($resolved)
    }

    $drawing = '{"type":"excalidraw","version":2,"elements":[],"appState":{},"files":{}}'
    [System.IO.File]::WriteAllText($firstPath, $drawing)
    [System.IO.File]::WriteAllText($secondPath, $drawing)
    [System.IO.File]::WriteAllText($requestPath, "run")

    Start-Process -FilePath $firstPath
    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadlineUtc -and -not $startedProcess) {
        $startedProcess = Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue |
            Sort-Object StartTime |
            Select-Object -First 1
        if (-not $startedProcess) {
            Start-Sleep -Milliseconds 250
        }
    }
    if (-not $startedProcess) {
        throw "Windows did not launch Excalidraw Desktop for the associated file."
    }
    Wait-ForTitle -Process $startedProcess -Prefix "activation-one.excalidraw"

    Start-Process -FilePath $secondPath
    Wait-ForTitle -Process $startedProcess -Prefix "activation-two.excalidraw"

    Start-Process -FilePath $firstPath
    Wait-ForTitle -Process $startedProcess -Prefix "activation-one.excalidraw"

    $root = [System.Windows.Automation.AutomationElement]::FromHandle(
        $startedProcess.MainWindowHandle)
    $tabCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $tabs = $root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $tabCondition)
    $processCount = @(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count
    if ($tabs.Count -ne 2 -or $processCount -ne 1) {
        throw "Expected two tabs in one process after repeated file activation; found $($tabs.Count) tabs and $processCount processes."
    }

    [pscustomobject]@{
        Result = "Passed"
        ProcessId = $startedProcess.Id
        RegisteredAssociation = ".excalidraw"
        ActivatedFiles = 2
        DuplicateFocused = $true
        SingleInstance = $true
    }
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
