[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $scriptRoot
$webRoot = Join-Path $repositoryRoot "ExcalidrawDesktop.Web"
$webOutput = Join-Path $webRoot "dist"
$nativeRoot = Join-Path $repositoryRoot "ExcalidrawDesktop.App"
$nativeWebAssets = Join-Path $nativeRoot "Assets\Web"
$nativeRootPath = [System.IO.Path]::GetFullPath($nativeRoot).TrimEnd('\') + '\'
$nativeWebAssetsPath = [System.IO.Path]::GetFullPath($nativeWebAssets)

if (-not $nativeWebAssetsPath.StartsWith(
    $nativeRootPath,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The generated web asset path is outside the WinUI project: $nativeWebAssetsPath"
}

Push-Location $repositoryRoot
try {
    Push-Location $webRoot
    try {
        & corepack yarn build
        if ($LASTEXITCODE -ne 0) {
            throw "Desktop web build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    Get-ChildItem -LiteralPath $nativeWebAssetsPath -Force |
        Where-Object { $_.Name -ne ".gitignore" } |
        Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $webOutput "*") -Destination $nativeWebAssetsPath -Recurse -Force
}
finally {
    Pop-Location
}
