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

function Get-RegisteredFileActivationArguments {
    param([Parameter(Mandatory)] $Application)

    $openWithPath =
        "Registry::HKEY_CURRENT_USER\Software\Classes\.excalidraw\OpenWithProgids"
    $openWith = Get-ItemProperty -LiteralPath $openWithPath -ErrorAction Stop
    foreach ($property in $openWith.PSObject.Properties) {
        if ($property.Name.StartsWith("PS", [System.StringComparison]::Ordinal) -or
            -not $property.Name.StartsWith("App.", [System.StringComparison]::Ordinal)) {
            continue
        }
        $commandPath =
            "Registry::HKEY_CURRENT_USER\Software\Classes\$($property.Name)\shell\open\command"
        $command = (Get-ItemProperty -LiteralPath $commandPath -ErrorAction SilentlyContinue).'(default)'
        if ($command -and $command.StartsWith(
                $Application.ExecutablePath,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            return $command.Substring($Application.ExecutablePath.Length).Trim()
        }
    }
    throw "The unpackaged app did not register an .excalidraw file activation command."
}

function Start-RegisteredFileActivation {
    param(
        [Parameter(Mandatory)] $Application,
        [Parameter(Mandatory)] [string] $RegisteredArguments,
        [Parameter(Mandatory)] [string] $Path
    )

    $arguments = $RegisteredArguments.Replace("%1", $Path)
    return Start-Process `
        -FilePath $Application.ExecutablePath `
        -WorkingDirectory $Application.InstallLocation `
        -ArgumentList $arguments `
        -PassThru
}

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Publish
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/publish command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the file activation smoke test."
    }

    $package = Get-DesktopTestApplication

    # A portable app registers its per-user file association on first launch.
    $registrationProcess = Start-DesktopTestApplication -Application $package
    Wait-ForTitle -Process $registrationProcess -Prefix "Excalidraw Desktop"
    Stop-DesktopTestProcess -Process $registrationProcess
    $registeredArguments = Get-RegisteredFileActivationArguments `
        -Application $package

    $installRoot = [System.IO.Path]::GetFullPath($package.InstallLocation).TrimEnd('\') + '\'
    $firstPath = Join-Path $package.InstallLocation "activation-one.excalidraw"
    $secondPath = Join-Path $package.InstallLocation "activation-two.excalidraw"
    $requestPath = Join-Path $package.InstallLocation "file-activation-smoke.request"
    $statePath = Join-Path $package.InstallLocation "file-activation-smoke-state.json"
    foreach ($path in @($firstPath, $secondPath, $requestPath, $statePath)) {
        $resolved = [System.IO.Path]::GetFullPath($path)
        if (-not $resolved.StartsWith($installRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "A file activation smoke path escaped the application directory: $resolved"
        }
        $generatedPaths.Add($resolved)
    }

    $drawing = '{"type":"excalidraw","version":2,"elements":[],"appState":{},"files":{}}'
    [System.IO.File]::WriteAllText($firstPath, $drawing)
    [System.IO.File]::WriteAllText($secondPath, $drawing)
    [System.IO.File]::WriteAllText($requestPath, "run")

    $startedProcess = Start-RegisteredFileActivation `
        -Application $package `
        -RegisteredArguments $registeredArguments `
        -Path $firstPath
    Wait-ForTitle -Process $startedProcess -Prefix "activation-one.excalidraw"

    $null = Start-RegisteredFileActivation `
        -Application $package `
        -RegisteredArguments $registeredArguments `
        -Path $secondPath
    Wait-ForTitle -Process $startedProcess -Prefix "activation-two.excalidraw"

    $null = Start-RegisteredFileActivation `
        -Application $package `
        -RegisteredArguments $registeredArguments `
        -Path $firstPath
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
