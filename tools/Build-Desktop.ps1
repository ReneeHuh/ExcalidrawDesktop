[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Debug",

    [ValidateSet("x64")]
    [string] $Platform = "x64",

    [switch] $SkipRestore,

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

Push-Location $repositoryRoot
try {
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
        -p:Platform=$Platform `
        -p:AppxPackageSigningEnabled=false
    if ($LASTEXITCODE -ne 0) {
        throw "WinUI build failed with exit code $LASTEXITCODE."
    }

    if ($Deploy -or $Launch) {
        $buildRoot = Join-Path $nativeRoot "bin\$Platform\$Configuration"
        $loosePackageRoot = $null
        $transformedManifest = Get-ChildItem -LiteralPath $buildRoot -Recurse -Filter "AppxManifest.xml" |
            Where-Object {
                $_.DirectoryName -notlike "*\AppX" -and
                (Test-Path -LiteralPath (Join-Path $_.DirectoryName "ExcalidrawDesktop.exe"))
            } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($transformedManifest) {
            $loosePackageRoot = Join-Path $transformedManifest.DirectoryName "AppX"
            if (Test-Path -LiteralPath $loosePackageRoot -PathType Container) {
                Get-ChildItem -LiteralPath $transformedManifest.DirectoryName -Force |
                    Where-Object { $_.Name -notin @("AppX", "publish") } |
                    Copy-Item -Destination $loosePackageRoot -Recurse -Force
            }
        }

        $appManifest = if ($loosePackageRoot) {
            Get-Item -LiteralPath (Join-Path $loosePackageRoot "AppxManifest.xml")
        }
        else {
            Get-ChildItem -LiteralPath $buildRoot -Recurse -Filter "AppxManifest.xml" |
                Where-Object { $_.DirectoryName -notlike "*\publish*" } |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1
        }

        if (-not $appManifest) {
            throw "The generated AppxManifest.xml could not be found below $buildRoot."
        }

        [xml] $manifestXml = Get-Content -LiteralPath $appManifest.FullName -Raw
        $packageName = $manifestXml.Package.Identity.Name
        $packageVersion = [version] $manifestXml.Package.Identity.Version
        $packageRoot = [System.IO.Path]::GetFullPath(
            $appManifest.DirectoryName).TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar)
        $registeredPackage = Get-AppxPackage -Name $packageName |
            Where-Object { $_.Version -eq $packageVersion } |
            Sort-Object Version -Descending |
            Select-Object -First 1
        $registeredRoot = if ($registeredPackage) {
            [System.IO.Path]::GetFullPath(
                $registeredPackage.InstallLocation).TrimEnd(
                    [System.IO.Path]::DirectorySeparatorChar)
        }

        if ($registeredPackage -and
            [string]::Equals(
                $registeredRoot,
                $packageRoot,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Host (
                "Development package {0} {1} is already registered from {2}." -f
                    $packageName,
                    $packageVersion,
                    $packageRoot)
        }
        else {
            Add-AppxPackage -Register $appManifest.FullName
        }
    }

    if ($Launch) {
        $package = Get-AppxPackage -Name "ExcalidrawDesktop.Development" |
            Sort-Object Version -Descending |
            Select-Object -First 1
        if (-not $package) {
            throw "The Excalidraw Desktop development package is not registered."
        }

        Start-Process `
            -FilePath "explorer.exe" `
            -ArgumentList "shell:AppsFolder\$($package.PackageFamilyName)!App" `
            -WindowStyle Hidden
    }
}
finally {
    Pop-Location
}
