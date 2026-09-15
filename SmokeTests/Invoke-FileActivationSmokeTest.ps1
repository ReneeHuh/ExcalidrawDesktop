[CmdletBinding()]
param(
    [ValidateRange(5, 120)] [int] $TimeoutSeconds = 30,
    [switch] $SkipBuild,
    [switch] $KeepRunning,
    # Allows the same activation checks against an installed Release payload.
    [object] $Application
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'SmokeTestCommon.ps1')
Add-Type -AssemblyName UIAutomationClient
$startedProcess = $null
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) "excalidraw-activation-$([guid]::NewGuid().ToString('N'))"

function Wait-ForTitle([Diagnostics.Process] $Process, [string] $Prefix) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) { throw 'Excalidraw Desktop exited during file activation.' }
        if ($Process.MainWindowTitle.StartsWith($Prefix, [StringComparison]::Ordinal)) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for '$Prefix'. Last title: $($Process.MainWindowTitle)"
}

try {
    if (-not $SkipBuild -and -not $Application) {
        & (Join-Path $repositoryRoot 'tools/Build-Desktop.ps1') -Configuration Debug -SkipRestore -Publish
        if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    }
    if (@(Get-Process -Name ExcalidrawDesktop -ErrorAction SilentlyContinue).Count) {
        throw 'Close Excalidraw Desktop before running the file activation test.'
    }
    if (-not $Application) { $Application = Get-DesktopTestApplication }
    $package = [pscustomobject]@{
        InstallLocation = $Application.InstallLocation
        ExecutablePath = $Application.ExecutablePath
        DataRoot = Join-Path $testDirectory 'data'
    }
    $null = New-Item -ItemType Directory -Path $testDirectory
    # Spaces and Unicode exercise the quoting used by the installer open command.
    $firstPath = Join-Path $testDirectory 'activation one.excalidraw'
    $secondName = 'activation ' + [char]0x65E5 + [char]0x672C + '.excalidraw'
    $secondPath = Join-Path $testDirectory $secondName
    $drawing = '{"type":"excalidraw","version":2,"elements":[],"appState":{},"files":{}}'
    [IO.File]::WriteAllText($firstPath, $drawing)
    [IO.File]::WriteAllText($secondPath, $drawing)

    # Smoke mode deliberately skips portable registry registration. Installed
    # association ownership is tested separately by Invoke-InstallerSmokeTest.
    $startedProcess = Start-DesktopTestApplication -Application $package -Arguments ('"{0}"' -f $firstPath)
    Wait-ForTitle $startedProcess 'activation one.excalidraw'
    foreach ($path in @($secondPath, $firstPath)) {
        $redirected = Start-DesktopTestApplication -Application $package -Arguments ('"{0}"' -f $path)
        Wait-ForTitle $startedProcess ([IO.Path]::GetFileName($path))
        if (-not $redirected.WaitForExit(10000)) { throw 'Redirected process did not exit.' }
    }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($startedProcess.MainWindowHandle)
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $processCount = @(Get-Process -Name ExcalidrawDesktop -ErrorAction SilentlyContinue).Count
    if ($tabs.Count -ne 2 -or $processCount -ne 1) {
        throw "Expected two tabs in one process; found $($tabs.Count) tabs and $processCount processes."
    }
    [pscustomobject]@{
        Result = 'Passed'; Activation = 'CommandLine'; ActivatedFiles = 2
        UnicodeAndSpaces = $true; DuplicateFocused = $true; SingleInstance = $true
    }
}
finally {
    if (-not $KeepRunning) {
        if ($startedProcess -and -not $startedProcess.HasExited) {
            $null = $startedProcess.CloseMainWindow()
            $null = $startedProcess.WaitForExit(5000)
        }
        Stop-DesktopTestProcess -Process $startedProcess
        $resolved = [IO.Path]::GetFullPath($testDirectory)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolved) -notlike 'excalidraw-activation-*') { throw 'Unsafe test cleanup path.' }
        for ($attempt = 0; $attempt -lt 10 -and (Test-Path -LiteralPath $resolved); $attempt++) {
            try { Remove-Item -LiteralPath $resolved -Recurse -Force }
            catch {
                if ($attempt -eq 9) { Write-Warning "Test data retained while WebView2 finishes closing: $resolved" }
                else { Start-Sleep -Milliseconds 500 }
            }
        }
    }
}
