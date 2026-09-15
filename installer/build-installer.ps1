#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,
    [string] $IsccPath,
    [string] $WebView2InstallerPath,
    [switch] $SkipBuild,
    # Trusted local script accepting -FilePath; must sign and timestamp that file.
    [string] $SigningScriptPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repositoryRoot 'ExcalidrawDesktop.App/ExcalidrawDesktop.App.csproj'
if (-not $Version) {
    $projectXml = [xml](Get-Content -LiteralPath $project -Raw)
    $Version = @($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0]
}
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or
    @($Version.Split('.') | Where-Object { [long]$_ -gt 65534 }).Count) {
    throw 'Version must have three numeric components between 0 and 65534, for example 0.1.4.'
}
$identity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'identity.json') -Raw | ConvertFrom-Json
$dependencies = & (Join-Path $PSScriptRoot 'Get-InstallerDependencies.ps1') `
    -IsccPath $IsccPath -WebView2InstallerPath $WebView2InstallerPath
if (-not $SkipBuild) {
    & (Join-Path $repositoryRoot 'tools/Build-Desktop.ps1') -Configuration Release -Publish -Version $Version
    if ($LASTEXITCODE -ne 0) { throw "Release publish failed: $LASTEXITCODE." }
}

$publishedRoot = Join-Path $repositoryRoot 'ExcalidrawDesktop.App/bin/x64/Release/unpacked'
$executable = Join-Path $publishedRoot 'ExcalidrawDesktop.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Publish Release before using -SkipBuild.' }
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion
if ([version]$fileVersion -ne [version]"$Version.0") {
    throw "Published version $fileVersion does not match requested installer version $Version. Rebuild without -SkipBuild."
}
$artifacts = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$stage = [IO.Path]::GetFullPath((Join-Path $artifacts "installer-work/$Version/payload"))
if (-not $stage.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Installer staging directory escaped artifacts.'
}
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$null = New-Item -ItemType Directory -Path $stage -Force
foreach ($file in Get-ChildItem -LiteralPath $publishedRoot -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($publishedRoot, $file.FullName)
    if ($relative -match '(?i)(^|[\\/])(\.smoke-data|UsageLogs|CrashLogs|WebView2)([\\/]|$)' -or
        $relative -match '(?i)(\.pdb$|\.excalidraw$|smoke.*\.(json|request)$)') { continue }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected link in publish output: $relative." }
    $destination = Join-Path $stage $relative
    $null = New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
foreach ($required in @('ExcalidrawDesktop.exe', 'ExcalidrawDesktop.dll', 'ExcalidrawDesktop.Core.dll',
    'Microsoft.UI.Xaml.dll', 'coreclr.dll', 'ExcalidrawDesktop.pri', 'App.xbf', 'Assets/Web/index.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $required) -PathType Leaf)) {
        throw "Required self-contained payload file missing: $required."
    }
}
foreach ($notice in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $notice) -Destination $stage
}
& (Join-Path $PSScriptRoot 'Copy-DependencyNotices.ps1') -Destination (Join-Path $stage 'Licenses')
[IO.File]::WriteAllText((Join-Path $stage $identity.markerFile), $identity.appId, [Text.Encoding]::ASCII)

if ($SigningScriptPath) {
    $SigningScriptPath = (Resolve-Path -LiteralPath $SigningScriptPath).Path
    foreach ($ownedBinary in @('ExcalidrawDesktop.exe', 'ExcalidrawDesktop.dll', 'ExcalidrawDesktop.Core.dll')) {
        $path = Join-Path $stage $ownedBinary
        & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $SigningScriptPath -FilePath $path
        if ($LASTEXITCODE -ne 0 -or (Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'Valid') {
            throw "Signing failed for $ownedBinary."
        }
    }
}
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File |
    ForEach-Object { [IO.Path]::GetRelativePath($stage, $_.FullName) }) + 'installed-files.txt'
[IO.File]::WriteAllLines((Join-Path $stage 'installed-files.txt'), ($files | Sort-Object), [Text.UTF8Encoding]::new($true))

$output = Join-Path $artifacts 'installer'
$null = New-Item -ItemType Directory -Path $output -Force
$setup = Join-Path $output "ExcalidrawDesktop-Setup-$Version-x64.exe"
# Remove only this build's previous outputs so a failed compile cannot appear successful.
foreach ($path in @($setup, "$setup.sha256", "$setup.build.json")) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
$arguments = @('/Qp', "/DAppVersion=$Version", "/DAppId=$($identity.appId)",
    "/DAppName=$($identity.appName)", "/DProgId=$($identity.progId)",
    "/DMarkerFile=$($identity.markerFile)", "/DMutexPrefix=$($identity.mutexPrefix)",
    "/DMaintenanceMutexPrefix=$($identity.maintenanceMutexPrefix)",
    "/DSourceDir=$stage", "/DOutputDir=$output",
    "/DWebView2Installer=$($dependencies.WebView2InstallerPath)", "/DWebView2SHA256=$($dependencies.WebView2SHA256)")
if ($SigningScriptPath) {
    $signCommand = '$q' + (Join-Path $PSHOME 'pwsh.exe') + '$q -NoProfile -File $q' + $SigningScriptPath + '$q -FilePath $f'
    $arguments += @('/DSigningEnabled=1', "/SDesktopSign=$signCommand")
}
& $dependencies.IsccPath @arguments (Join-Path $PSScriptRoot 'ExcalidrawDesktop.iss')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup)) { throw "Installer compilation failed: $LASTEXITCODE." }
if ($SigningScriptPath -and (Get-AuthenticodeSignature -LiteralPath $setup).Status -ne 'Valid') {
    throw 'Setup signature verification failed.'
}
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$setup.sha256", "$hash  $([IO.Path]::GetFileName($setup))`n", [Text.Encoding]::ASCII)
$commit = & git -C $repositoryRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { $commit = 'unknown' }
$dirty = @(& git -C $repositoryRoot status --porcelain).Count -gt 0
[ordered]@{
    appVersion = $Version; architecture = 'x64'; builtUtc = [DateTime]::UtcNow.ToString('o')
    sourceCommit = $commit; sourceDirty = $dirty; setupSHA256 = $hash
    signed = [bool]$SigningScriptPath; innoSetupVersion = $dependencies.CompilerVersion
    webView2InstallerVersion = $dependencies.WebView2InstallerVersion; webView2SHA256 = $dependencies.WebView2SHA256
    payloadFileCount = $files.Count
} | ConvertTo-Json | Set-Content -LiteralPath "$setup.build.json" -Encoding utf8
Write-Host "Installer: $setup"
Write-Host "SHA256: $hash"
Get-Item -LiteralPath $setup
