[CmdletBinding()]
param([string] $PublishDirectory, [int] $TimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $PublishDirectory) {
    $PublishDirectory = Join-Path $repositoryRoot 'ExcalidrawDesktop.App\bin\x64\Debug\unpacked'
}
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$executable = Join-Path $PublishDirectory 'ExcalidrawDesktop.exe'
$dataRoot = Join-Path $repositoryRoot ('artifacts\close-reopen-smoke\' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($dataRoot) | Out-Null
$previousDataRoot = $env:EXCALIDRAW_DESKTOP_DATA_ROOT
$previousSmoke = $env:EXCALIDRAW_DESKTOP_SMOKE_TEST
$env:EXCALIDRAW_DESKTOP_DATA_ROOT = $dataRoot
$env:EXCALIDRAW_DESKTOP_SMOKE_TEST = '1'
try {
    foreach ($stage in @('close-decisions-smoke', 'close-reopen-restore')) {
        $result = Join-Path $PublishDirectory "$stage.result"
        if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
        [IO.File]::WriteAllText((Join-Path $PublishDirectory "$stage.request"), 'run')
        $process = Start-Process -FilePath $executable -WorkingDirectory $PublishDirectory -WindowStyle Hidden -PassThru
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            do {
                Start-Sleep -Milliseconds 200
                $process.Refresh()
                $text = if (Test-Path -LiteralPath $result) { [IO.File]::ReadAllText($result) } else { '' }
                if ($text.StartsWith('failed:')) { throw $text }
                if ($stage -eq 'close-decisions-smoke' -and $text -eq 'exit-pending' -and $process.HasExited) { break }
                if ($stage -eq 'close-reopen-restore' -and $text -eq 'passed') { break }
                if ($process.HasExited) { throw "App exited before $stage finished. Result: $text" }
                if ([DateTime]::UtcNow -ge $deadline) { throw "$stage timed out. Result: $text" }
            } while ($true)
            if ($stage -eq 'close-decisions-smoke') {
                $state = Get-Content -LiteralPath (Join-Path $dataRoot 'close-decisions-smoke-state.json') -Raw | ConvertFrom-Json
                if ($state.Windows.Count -ne 2 -or $state.ClosedItems.Count -ne 1 -or
                    @($state.Windows | Where-Object { -not $_.RestoreAllTabs }).Count -ne 0) {
                    throw 'Exit persisted an incorrect startup workspace.'
                }
            }
            Write-Output "$stage passed"
        }
        finally {
            $process.Refresh()
            if (-not $process.HasExited) {
                $owned = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
                if ($owned -and $owned.Path -eq $executable) { Stop-Process -Id $owned.Id }
            }
        }
    }
    Write-Output "Recovery and log artifacts: $dataRoot"
}
finally {
    $env:EXCALIDRAW_DESKTOP_DATA_ROOT = $previousDataRoot
    $env:EXCALIDRAW_DESKTOP_SMOKE_TEST = $previousSmoke
}
