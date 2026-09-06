$ErrorActionPreference = "Stop"

function Get-DesktopTestApplication {
    param(
        [ValidateSet("Debug", "Release")]
        [string] $Configuration = "Debug"
    )

    $testRoot = Split-Path -Parent $PSCommandPath
    $repositoryRoot = Split-Path -Parent $testRoot
    $installLocation = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "ExcalidrawDesktop.App\bin\x64\$Configuration\unpacked"))
    $executablePath = Join-Path $installLocation "ExcalidrawDesktop.exe"
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "The unpackaged desktop executable was not found at $executablePath. Run without -SkipBuild."
    }

    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        $executablePath).ProductVersion
    return [pscustomobject]@{
        InstallLocation = $installLocation
        ExecutablePath = $executablePath
        Version = $version
        DataRoot = Join-Path $installLocation ".smoke-data"
    }
}

function Start-DesktopTestApplication {
    param(
        [Parameter(Mandatory)] $Application,
        [string] $Arguments = ""
    )

    $previousDataRoot = [Environment]::GetEnvironmentVariable(
        "EXCALIDRAW_DESKTOP_DATA_ROOT",
        [EnvironmentVariableTarget]::Process)
    $previousSmokeMode = [Environment]::GetEnvironmentVariable(
        "EXCALIDRAW_DESKTOP_SMOKE_TEST", [EnvironmentVariableTarget]::Process)
    try {
        [Environment]::SetEnvironmentVariable(
            "EXCALIDRAW_DESKTOP_SMOKE_TEST", "1", [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            "EXCALIDRAW_DESKTOP_DATA_ROOT",
            $Application.DataRoot,
            [EnvironmentVariableTarget]::Process)
        $startParameters = @{
            FilePath = $Application.ExecutablePath
            WorkingDirectory = $Application.InstallLocation
            PassThru = $true
            WindowStyle = "Hidden"
        }
        if (-not [string]::IsNullOrWhiteSpace($Arguments)) {
            $startParameters.ArgumentList = $Arguments
        }
        return Start-Process @startParameters
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            "EXCALIDRAW_DESKTOP_SMOKE_TEST", $previousSmokeMode, [EnvironmentVariableTarget]::Process)
        [Environment]::SetEnvironmentVariable(
            "EXCALIDRAW_DESKTOP_DATA_ROOT",
            $previousDataRoot,
            [EnvironmentVariableTarget]::Process)
    }
}

function Wait-DesktopWindowTitle {
    param(
        [Parameter(Mandatory)] [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [string] $ExpectedTitle,
        [Parameter(Mandatory)] [DateTime] $DeadlineUtc,
        [string] $FailurePrefix
    )

    while ([DateTime]::UtcNow -lt $DeadlineUtc) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Excalidraw Desktop exited before reaching '$ExpectedTitle'."
        }
        if ($Process.MainWindowTitle -eq $ExpectedTitle) {
            return
        }
        if ($FailurePrefix -and $Process.MainWindowTitle.StartsWith(
                $FailurePrefix,
                [System.StringComparison]::Ordinal)) {
            throw $Process.MainWindowTitle
        }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for '$ExpectedTitle'. Last title: $($Process.MainWindowTitle)"
}

function Stop-DesktopTestProcess {
    param([System.Diagnostics.Process] $Process)

    if (-not $Process) {
        return
    }
    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force
            $null = $Process.WaitForExit(5000)
        }
    }
    catch [System.InvalidOperationException] {
        # The test-owned process already exited.
    }
}
