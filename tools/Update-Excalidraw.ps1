[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [switch] $SkipDesktopBuild
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $scriptRoot
$webRoot = Join-Path $repositoryRoot "ExcalidrawDesktop.Web"
$webPackagePath = Join-Path $webRoot "package.json"
$solutionPath = Join-Path $repositoryRoot "ExcalidrawDesktop.sln"
$buildScript = Join-Path $scriptRoot "Build-Desktop.ps1"

Push-Location $repositoryRoot
try {
    $packageContent = [System.IO.File]::ReadAllText($webPackagePath)
    $dependencyPattern = '("@excalidraw/excalidraw"\s*:\s*")[^"]+("\s*)'
    $dependencyMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $packageContent,
        $dependencyPattern)
    if ($dependencyMatches.Count -ne 1) {
        throw "Expected exactly one @excalidraw/excalidraw dependency in $webPackagePath."
    }

    $updatedPackageContent = [System.Text.RegularExpressions.Regex]::Replace(
        $packageContent,
        $dependencyPattern,
        "`${1}$Version`${2}")
    [System.IO.File]::WriteAllText(
        $webPackagePath,
        $updatedPackageContent,
        [System.Text.UTF8Encoding]::new($false))

    Push-Location $webRoot
    try {
        & corepack yarn install --force --non-interactive
        if ($LASTEXITCODE -ne 0) {
            throw "Updating @excalidraw/excalidraw failed with exit code $LASTEXITCODE."
        }

        & corepack yarn typecheck
        if ($LASTEXITCODE -ne 0) {
            throw "Desktop web type-check failed with exit code $LASTEXITCODE."
        }

        & corepack yarn test
        if ($LASTEXITCODE -ne 0) {
            throw "Desktop web tests failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }

    & dotnet test $solutionPath -p:Platform=x64
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop .NET tests failed with exit code $LASTEXITCODE."
    }

    if (-not $SkipDesktopBuild) {
        & $buildScript -SkipRestore
        if ($LASTEXITCODE -ne 0) {
            throw "Desktop build failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    Pop-Location
}
