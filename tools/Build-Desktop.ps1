[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [ValidateSet("x64")]
    [string] $Platform = "x64",

    [switch] $SkipRestore,

    [switch] $Publish,

    # Compatibility alias for existing automation. Unpackaged builds are
    # published to a folder; there is no package-registration deployment step.
    [switch] $Deploy,

    [switch] $Launch
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $scriptRoot
$webRoot = Join-Path $repositoryRoot "ExcalidrawDesktop.Web"
$nativeRoot = Join-Path $repositoryRoot "ExcalidrawDesktop.App"
$projectPath = Join-Path $nativeRoot "ExcalidrawDesktop.App.csproj"
$webAssetsBuildScript = Join-Path $scriptRoot "Build-WebAssets.ps1"
$localizationTestScript = Join-Path $scriptRoot "Test-Localization.ps1"
$publishRoot = Join-Path $nativeRoot "bin\$Platform\$Configuration\unpacked"

Push-Location $repositoryRoot
try {
    & $localizationTestScript

    if (-not $SkipRestore) {
        Push-Location $webRoot
        try {
            & corepack yarn install --frozen-lockfile
            if ($LASTEXITCODE -ne 0) {
                throw "Yarn restore failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
    }

    & $webAssetsBuildScript
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop web asset build failed with exit code $LASTEXITCODE."
    }

    & dotnet build $projectPath `
        --configuration $Configuration `
        -p:Platform=$Platform
    if ($LASTEXITCODE -ne 0) {
        throw "WinUI build failed with exit code $LASTEXITCODE."
    }

    if ($Publish -or $Deploy -or $Launch) {
        $resolvedNativeRoot = [IO.Path]::GetFullPath($nativeRoot).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $resolvedPublishRoot = [IO.Path]::GetFullPath($publishRoot)
        if (-not $resolvedPublishRoot.StartsWith(
            $resolvedNativeRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean publish output outside $resolvedNativeRoot."
        }

        if (Test-Path -LiteralPath $resolvedPublishRoot) {
            Remove-Item -LiteralPath $resolvedPublishRoot -Recurse -Force
        }

        & dotnet publish $projectPath `
            --configuration $Configuration `
            --runtime "win-$Platform" `
            --self-contained true `
            --output $publishRoot `
            -p:Platform=$Platform `
            -p:WindowsPackageType=None `
            -p:WindowsAppSDKSelfContained=true
        if ($LASTEXITCODE -ne 0) {
            throw "Unpackaged WinUI publish failed with exit code $LASTEXITCODE."
        }

        $executablePath = Join-Path $publishRoot "ExcalidrawDesktop.exe"
        if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
            throw "The published executable was not found at $executablePath."
        }

        Write-Host "Unpackaged application published to $publishRoot."
    }

    if ($Launch) {
        Start-Process -FilePath $executablePath -WorkingDirectory $publishRoot
    }
}
finally {
    Pop-Location
}
