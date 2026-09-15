[CmdletBinding()]
param(
    [ValidateRange(15, 180)] [int] $TimeoutSeconds = 120,
    [switch] $SkipBuild,
    [switch] $KeepRunning
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    & (Join-Path $repositoryRoot 'tools/Build-Desktop.ps1') -Configuration Debug -SkipRestore -Publish
    if ($LASTEXITCODE -ne 0) { throw 'The desktop build failed.' }
}
# Close now preserves drafts. Exercise the real WebViews and native controllers,
# including library decisions and process restart.
& (Join-Path $repositoryRoot 'tools/Test-CloseReopen.ps1') -TimeoutSeconds $TimeoutSeconds
