#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $IsccPath,
    [string] $WebView2InstallerPath
)

$ErrorActionPreference = 'Stop'
$pins = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
$cache = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/installer-tools'
$null = New-Item -ItemType Directory -Path $cache -Force

function Assert-Publisher([string] $Path, [string] $Publisher) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch $Publisher) {
        throw "Dependency signature is not valid for the expected publisher: $Path ($($signature.Status))."
    }
}

if (-not $IsccPath) {
    $IsccPath = Join-Path $cache 'Inno Setup 6/ISCC.exe'
    if (-not (Test-Path -LiteralPath $IsccPath)) {
        $setupPath = Join-Path $cache 'innosetup-6.7.3.exe'
        if (-not (Test-Path -LiteralPath $setupPath)) {
            Write-Host 'Downloading Inno Setup 6.7.3...'
            Invoke-WebRequest $pins.innoSetup.url -OutFile $setupPath
        }
        $expectedHash = $pins.innoSetup.sha256
        if ((Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash -ne $expectedHash) {
            throw "Inno Setup download hash mismatch. Remove $setupPath and retry."
        }
        Assert-Publisher $setupPath 'CN=Pyrsys B\.V\.'
        $process = Start-Process -FilePath $setupPath -ArgumentList @(
            '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
            ('/DIR="{0}"' -f (Split-Path $IsccPath -Parent))
        ) -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)." }
    }
}
$IsccPath = (Resolve-Path -LiteralPath $IsccPath).Path
Assert-Publisher $IsccPath 'CN=Pyrsys B\.V\.'
# The compiler's PE version is 0.0.0.0. The .iss validates the actual ISPP Ver constant.
$compilerVersion = '6.7.3'

if (-not $WebView2InstallerPath) {
    $WebView2InstallerPath = Join-Path $cache 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
    if (-not (Test-Path -LiteralPath $WebView2InstallerPath)) {
        Write-Host 'Downloading the offline Microsoft Edge WebView2 Runtime installer...'
        Invoke-WebRequest $pins.webView2.url -OutFile $WebView2InstallerPath
    }
}
$WebView2InstallerPath = (Resolve-Path -LiteralPath $WebView2InstallerPath).Path
Assert-Publisher $WebView2InstallerPath 'CN=Microsoft Corporation(?:,|$)'
$runtimeHash = (Get-FileHash -LiteralPath $WebView2InstallerPath -Algorithm SHA256).Hash
if ($runtimeHash -ne $pins.webView2.sha256) {
    throw 'The WebView2 package does not match the pinned official x64 standalone download. Update installer/dependencies.json deliberately when refreshing the runtime.'
}
$runtimeInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($WebView2InstallerPath)
if ((Get-Item -LiteralPath $WebView2InstallerPath).Length -lt 20MB -or
    $runtimeInfo.ProductName -ne 'Microsoft Edge Update') {
    throw 'Provide the WebView2 Evergreen standalone x64 installer, not the small online bootstrapper.'
}

[pscustomobject]@{
    IsccPath = $IsccPath
    CompilerVersion = $compilerVersion
    WebView2InstallerPath = $WebView2InstallerPath
    WebView2InstallerVersion = $runtimeInfo.ProductVersion
    WebView2SHA256 = $runtimeHash
}
