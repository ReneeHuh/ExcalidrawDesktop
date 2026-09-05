[CmdletBinding()]
param(
    [ValidateRange(1, 20)]
    [int[]] $TabCounts = @(1, 5, 10, 20),

    [ValidateRange(15, 600)]
    [int] $TimeoutSeconds = 180,

    [string] $OutputPath,

    [switch] $SuspendInactiveTabs,

    [switch] $UnloadInactiveTabs,

    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
$testRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $testRoot
$buildScript = Join-Path $repositoryRoot "tools\Build-Desktop.ps1"
. (Join-Path $testRoot "SmokeTestCommon.ps1")
if ($SuspendInactiveTabs -and $UnloadInactiveTabs) {
    throw "Choose either -SuspendInactiveTabs or -UnloadInactiveTabs, not both."
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $reportName = if ($UnloadInactiveTabs) {
        "MULTI_TAB_PERFORMANCE_UNLOADED.json"
    }
    elseif ($SuspendInactiveTabs) {
        "MULTI_TAB_PERFORMANCE_SUSPENDED.json"
    }
    else {
        "MULTI_TAB_PERFORMANCE.json"
    }
    $OutputPath = Join-Path $repositoryRoot "docs\validation\$reportName"
}
$measurements = [System.Collections.Generic.List[object]]::new()
$startedProcess = $null
$requestPath = $null
$resultPath = $null
$statePath = $null

function Get-ProcessTreeMetrics {
    param([int] $RootProcessId)

    $processes = @(Get-CimInstance Win32_Process)
    $processIds = [System.Collections.Generic.HashSet[uint32]]::new()
    $null = $processIds.Add([uint32]$RootProcessId)
    do {
        $added = $false
        foreach ($process in $processes) {
            if ($processIds.Contains([uint32]$process.ParentProcessId) -and
                $processIds.Add([uint32]$process.ProcessId)) {
                $added = $true
            }
        }
    } while ($added)

    $tree = @($processes | Where-Object {
        $processIds.Contains([uint32]$_.ProcessId)
    })
    [pscustomobject]@{
        ProcessCount = $tree.Count
        WorkingSetBytes = [long](($tree | Measure-Object WorkingSetSize -Sum).Sum)
        PrivateMemoryBytes = [long](($tree | Measure-Object PrivatePageCount -Sum).Sum)
    }
}

try {
    if (-not $SkipBuild) {
        & $buildScript -Configuration Debug -SkipRestore -Publish
        if ($LASTEXITCODE -ne 0) {
            throw "The desktop build/publish command failed with exit code $LASTEXITCODE."
        }
    }

    if (@(Get-Process -Name "ExcalidrawDesktop" -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Close existing Excalidraw Desktop processes before running the performance test."
    }

    $package = Get-DesktopTestApplication

    $requestPath = Join-Path $package.InstallLocation "performance-smoke.request"
    $resultPath = Join-Path $package.InstallLocation "performance-smoke-result.json"
    $statePath = Join-Path $package.InstallLocation "performance-smoke-state.json"

    foreach ($tabCount in ($TabCounts | Sort-Object -Unique)) {
        foreach ($path in @($requestPath, $resultPath, $statePath)) {
            if ([System.IO.File]::Exists($path)) {
                [System.IO.File]::Delete($path)
            }
        }

        $request = if ($UnloadInactiveTabs) {
            "$tabCount`:unload"
        }
        elseif ($SuspendInactiveTabs) {
            "$tabCount`:suspend"
        }
        else {
            $tabCount.ToString()
        }
        [System.IO.File]::WriteAllText($requestPath, $request)
        $launchStarted = [System.Diagnostics.Stopwatch]::StartNew()
        $startedProcess = Start-DesktopTestApplication -Application $package
        $deadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

        while ([DateTime]::UtcNow -lt $deadlineUtc) {
            $startedProcess.Refresh()
            if ($startedProcess.HasExited) {
                throw "Excalidraw Desktop exited during the $tabCount-tab run."
            }
            if ($startedProcess.MainWindowTitle -eq "Excalidraw Desktop — Performance smoke failed") {
                throw "The unpackaged $tabCount-tab performance run failed."
            }
            if ($startedProcess.MainWindowTitle -eq "Excalidraw Desktop — Performance smoke passed" -and
                [System.IO.File]::Exists($resultPath)) {
                break
            }
            Start-Sleep -Milliseconds 250
        }

        if (-not [System.IO.File]::Exists($resultPath)) {
            throw "Timed out waiting for the $tabCount-tab result. Last title: $($startedProcess.MainWindowTitle)"
        }

        $launchStarted.Stop()
        $measurement = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        $processTree = Get-ProcessTreeMetrics -RootProcessId $startedProcess.Id
        $measurement | Add-Member -NotePropertyName launchToResultMilliseconds `
            -NotePropertyValue $launchStarted.Elapsed.TotalMilliseconds
        $measurement | Add-Member -NotePropertyName processTreeProcessCount `
            -NotePropertyValue $processTree.ProcessCount
        $measurement | Add-Member -NotePropertyName processTreeWorkingSetBytes `
            -NotePropertyValue $processTree.WorkingSetBytes
        $measurement | Add-Member -NotePropertyName processTreePrivateMemoryBytes `
            -NotePropertyValue $processTree.PrivateMemoryBytes
        $measurements.Add($measurement)

        Stop-Process -Id $startedProcess.Id -Force
        $null = $startedProcess.WaitForExit(5000)
        $startedProcess = $null
    }

    $report = [ordered]@{
        recordedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        machine = $env:COMPUTERNAME
        osVersion = [System.Environment]::OSVersion.VersionString
        applicationVersion = $package.Version
        configuration = if ($UnloadInactiveTabs) {
            "Debug x64 unpackaged; inactive clean editors unloaded"
        }
        elseif ($SuspendInactiveTabs) {
            "Debug x64 unpackaged; inactive clean tabs suspended"
        }
        else {
            "Debug x64 unpackaged; all tabs live"
        }
        measurements = $measurements
    }
    $outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = [System.IO.Path]::GetDirectoryName($outputFullPath)
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    [System.IO.File]::WriteAllText(
        $outputFullPath,
        ($report | ConvertTo-Json -Depth 5))

    $measurements |
        Select-Object tabCount,
            @{N="EditorReadyMs";E={[math]::Round($_.startupMilliseconds, 1)}},
            @{N="LaunchToResultMs";E={[math]::Round($_.launchToResultMilliseconds, 1)}},
            @{N="AverageSwitchMs";E={[math]::Round($_.averageSwitchMilliseconds, 2)}},
            @{N="MaximumSwitchMs";E={[math]::Round($_.maximumSwitchMilliseconds, 2)}},
            @{N="SuspendedTabs";E={$_.suspendedTabCount}},
            @{N="UnloadedTabs";E={$_.unloadedTabCount}},
            @{N="ProcessCount";E={$_.processTreeProcessCount}},
            @{N="TreeWorkingSetMB";E={[math]::Round($_.processTreeWorkingSetBytes / 1MB, 1)}},
            @{N="TreePrivateMemoryMB";E={[math]::Round($_.processTreePrivateMemoryBytes / 1MB, 1)}}
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

    foreach ($path in @($requestPath, $resultPath, $statePath)) {
        if ($path -and [System.IO.File]::Exists($path)) {
            [System.IO.File]::Delete($path)
        }
    }
}
