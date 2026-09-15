#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $Destination)

$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$null = New-Item -ItemType Directory -Path $Destination -Force
Push-Location (Join-Path $repositoryRoot 'ExcalidrawDesktop.Web')
try {
    $disclaimer = & corepack yarn --silent licenses generate-disclaimer --production
    if ($LASTEXITCODE -ne 0 -or -not $disclaimer) { throw 'Generating npm dependency notices failed.' }
    $disclaimer | Set-Content -LiteralPath (Join-Path $Destination 'WEB-THIRD-PARTY.txt') -Encoding utf8
}
finally { Pop-Location }

$assets = Get-Content -LiteralPath (Join-Path $repositoryRoot 'ExcalidrawDesktop.App/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$packages = @($assets.libraries.GetEnumerator() | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Value.path })
foreach ($framework in $assets.project.frameworks.Values) {
    foreach ($dependency in $framework.downloadDependencies) {
        $version = $dependency.version.Trim('[', ']').Split(',')[0].Trim()
        $packages += "$($dependency.name.ToLowerInvariant())/$version"
    }
}
foreach ($package in ($packages | Sort-Object -Unique)) {
    $packageRoot = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $package } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container })[0]
    if (-not $packageRoot) { throw "Restored package missing while collecting notices: $package" }
    $target = Join-Path $Destination "NuGet/$package"
    $null = New-Item -ItemType Directory -Path $target -Force
    $noticeFiles = @(Get-ChildItem -LiteralPath $packageRoot -File |
        Where-Object { $_.Name -match '(?i)(license|notice|copying|\.nuspec$)' })
    # Some packages keep the declared license in a subdirectory.
    foreach ($spec in Get-ChildItem -LiteralPath $packageRoot -Filter '*.nuspec' -File) {
        $metadata = ([xml](Get-Content -LiteralPath $spec.FullName -Raw)).package.metadata
        if ($metadata.license.type -eq 'file') {
            $licensePath = [IO.Path]::GetFullPath((Join-Path $packageRoot $metadata.license.InnerText))
            if (-not $licensePath.StartsWith([IO.Path]::GetFullPath($packageRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw "Package license escaped package directory: $package"
            }
            $noticeFiles += Get-Item -LiteralPath $licensePath
        }
    }
    foreach ($file in ($noticeFiles | Sort-Object FullName -Unique)) {
        $relative = [IO.Path]::GetRelativePath($packageRoot, $file.FullName)
        $path = Join-Path $target $relative
        $null = New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force
        Copy-Item -LiteralPath $file.FullName -Destination $path
    }
}
