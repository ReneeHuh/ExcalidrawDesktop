[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [ValidateRange(5, 120)]
    [int] $TimeoutSeconds = 30,

    [switch] $SkipBuild,

    [switch] $Restore,

    [switch] $KeepRunning
)

$ErrorActionPreference = "Stop"
$testStartedUtc = [DateTime]::UtcNow
$testRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $testRoot
$buildScript = Join-Path $repositoryRoot "tools\Build-Desktop.ps1"
$nativeRoot = Join-Path $repositoryRoot "ExcalidrawDesktop.App"
$expectedBuildRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $nativeRoot "bin\x64\$Configuration"))
$startedProcess = $null
$requestPath = $null
$statePath = $null
$observedTitles = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)

if ($SkipBuild -and $Restore) {
    throw "-Restore cannot be combined with -SkipBuild."
}

function Get-StartupDiagnostics {
    param(
        [DateTime] $SinceUtc
    )

    $events = Get-WinEvent -FilterHashtable @{
        LogName = "Application"
        StartTime = $SinceUtc.ToLocalTime()
    } -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProviderName -in @(".NET Runtime", "Application Error", "Windows Error Reporting") -and
            $_.Message -match "ExcalidrawDesktop"
        } |
        Select-Object -First 3

    if (-not $events) {
        return "No matching Application event-log entries were recorded."
    }

    return ($events | ForEach-Object {
        "[$($_.TimeCreated.ToString('O'))] $($_.ProviderName) $($_.Id): $($_.Message)"
    }) -join [Environment]::NewLine
}

try {
    if (-not $SkipBuild) {
        $buildArguments = @{
            Configuration = $Configuration
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
        throw "The Excalidraw Desktop development package is not registered. Run without -SkipBuild."
    }

    $installLocation = [System.IO.Path]::GetFullPath($package.InstallLocation)
    if (-not $installLocation.StartsWith(
        $expectedBuildRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The registered package points outside this build tree: $installLocation"
    }

    $existingProcesses = @(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue)
    if ($existingProcesses.Count -gt 0) {
        $processIds = ($existingProcesses.Id | Sort-Object) -join ", "
        throw "Close the existing Excalidraw Desktop process(es) before running the smoke test: $processIds"
    }

    $requestPath = Join-Path $installLocation "startup-smoke.request"
    $statePath = Join-Path $installLocation "startup-smoke-state.json"
    if ([System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
    [System.IO.File]::WriteAllText($requestPath, "run")

    Start-Process `
        -FilePath "explorer.exe" `
        -ArgumentList "shell:AppsFolder\$($package.PackageFamilyName)!App" `
        -WindowStyle Hidden

    $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadlineUtc) {
        if (-not $startedProcess) {
            $startedProcess = Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue |
                Select-Object -First 1
        }

        if ($startedProcess) {
            try {
                $startedProcess.Refresh()
                if ($startedProcess.HasExited) {
                    throw "Excalidraw Desktop exited before reporting bridge readiness."
                }

                if (-not [string]::IsNullOrWhiteSpace($startedProcess.MainWindowTitle)) {
                    $null = $observedTitles.Add($startedProcess.MainWindowTitle)
                }

                if ($startedProcess.Responding -and
                    $startedProcess.MainWindowTitle -eq "Excalidraw Desktop") {
                    $elapsed = [DateTime]::UtcNow - $testStartedUtc
                    [pscustomobject]@{
                        Result = "Passed"
                        ProcessId = $startedProcess.Id
                        Package = $package.PackageFullName
                        InstallLocation = $installLocation
                        ReadyTitle = $startedProcess.MainWindowTitle
                        ElapsedMilliseconds = [Math]::Round($elapsed.TotalMilliseconds)
                    }
                    return
                }
            }
            catch [System.InvalidOperationException] {
                throw "Excalidraw Desktop exited before reporting bridge readiness."
            }
        }

        Start-Sleep -Milliseconds 250
    }

    $titles = if ($observedTitles.Count -gt 0) {
        ($observedTitles | Sort-Object) -join ", "
    } else {
        "<none>"
    }
    $diagnostics = Get-StartupDiagnostics -SinceUtc $testStartedUtc
    throw "Timed out after $TimeoutSeconds seconds waiting for app.ready. Observed titles: $titles`n$diagnostics"
}
catch {
    $diagnostics = Get-StartupDiagnostics -SinceUtc $testStartedUtc
    Write-Error "$($_.Exception.Message)`n$diagnostics"
    exit 1
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
            # The test-owned process already exited; no cleanup remains.
        }
    }
    if ($requestPath -and [System.IO.File]::Exists($requestPath)) {
        [System.IO.File]::Delete($requestPath)
    }
    if ($statePath -and [System.IO.File]::Exists($statePath)) {
        [System.IO.File]::Delete($statePath)
    }
}
