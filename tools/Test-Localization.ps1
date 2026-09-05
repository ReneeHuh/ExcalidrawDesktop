[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $PSCommandPath
$repositoryRoot = Split-Path -Parent $scriptRoot
$appRoot = Join-Path $repositoryRoot "ExcalidrawDesktop.App"
$stringsRoot = Join-Path $repositoryRoot "ExcalidrawDesktop.App\Strings"
$expectedLocales = @(
    "en-US",
    "es-ES",
    "fr-FR",
    "de-DE",
    "pt-BR",
    "ja-JP",
    "zh-CN",
    "ar-SA"
)

function Read-Catalog([string] $locale) {
    $path = Join-Path $stringsRoot "$locale\Resources.resw"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing native localization catalog: $path"
    }

    [xml] $document = Get-Content -LiteralPath $path -Raw
    $catalog = @{}
    foreach ($entry in $document.root.data) {
        $key = [string] $entry.name
        $value = [string] $entry.value
        if ([string]::IsNullOrWhiteSpace($key) -or
            [string]::IsNullOrWhiteSpace($value)) {
            throw "Catalog $locale contains an empty key or value."
        }
        if ($catalog.ContainsKey($key)) {
            throw "Catalog $locale contains duplicate key '$key'."
        }
        $catalog[$key] = $value
    }
    return $catalog
}

$canonical = Read-Catalog "en-US"

$csharpFiles = Get-ChildItem -LiteralPath $appRoot -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' }
$csharpSource = ($csharpFiles | ForEach-Object {
    Get-Content -LiteralPath $_.FullName -Raw
}) -join "`n"
$resourceCalls = [regex]::Matches(
    $csharpSource,
    'DesktopResources\.(?:Get|Format)\(\s*"([^"]+)"')
$referencedKeys = @(
    $resourceCalls |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique)
$missingReferencedKeys = @(
    $referencedKeys | Where-Object { -not $canonical.ContainsKey($_) })
if ($missingReferencedKeys.Count -gt 0) {
    throw (
        "Native code references keys missing from en-US: [{0}]." -f
            ($missingReferencedKeys -join ", "))
}

$hardCodedUiAssignments = @(
    [regex]::Matches(
        $csharpSource,
        '(?m)^\s*(?:Title|Content|Text)\s*=\s*"[^"\r\n]+"') |
        ForEach-Object Value |
        Where-Object {
            $_ -notmatch '(?i)smoke' -and
            $_ -notmatch 'Excalidraw Desktop — Recovery snapshot saved'
        })
if ($hardCodedUiAssignments.Count -gt 0) {
    throw (
        "Native code contains resource-bypassing UI text: [{0}]." -f
            ($hardCodedUiAssignments -join "; "))
}

$xamlFiles = Get-ChildItem -LiteralPath $appRoot -Recurse -Filter "*.xaml" |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' }
foreach ($xamlFile in $xamlFiles) {
    $xaml = Get-Content -LiteralPath $xamlFile.FullName -Raw
    $visibleTextElements = [regex]::Matches(
        $xaml,
        '<[^>]+(?:Text|Content|Header|Description|Title|AutomationProperties\.Name)="[A-Za-z][^"]*"[^>]*>',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($element in $visibleTextElements) {
        if ($element.Value -notmatch '\bx:Uid="[^"]+"') {
            throw (
                "XAML UI text in {0} is missing x:Uid: {1}" -f
                    $xamlFile.FullName,
                    $element.Value.Trim())
        }
    }

    $uids = [regex]::Matches($xaml, '\bx:Uid="([^"]+)"')
    foreach ($uidMatch in $uids) {
        $uid = $uidMatch.Groups[1].Value
        $hasResource = @($canonical.Keys | Where-Object {
            $_ -eq $uid -or $_.StartsWith("$uid.")
        }).Count -gt 0
        if (-not $hasResource) {
            throw "XAML x:Uid '$uid' in $($xamlFile.FullName) has no en-US resource."
        }
    }
}

function Get-PlaceholderSignature([string] $value) {
    return @(
        [regex]::Matches($value, '\{(\d+)(?:[^{}]*)\}') |
            ForEach-Object { [int] $_.Groups[1].Value } |
            Sort-Object) -join "|"
}

foreach ($locale in $expectedLocales) {
    $catalog = Read-Catalog $locale
    $missing = @($canonical.Keys | Where-Object { -not $catalog.ContainsKey($_) })
    $unexpected = @($catalog.Keys | Where-Object { -not $canonical.ContainsKey($_) })
    if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
        throw (
            "Catalog {0} does not match en-US. Missing: [{1}]. Unexpected: [{2}]." -f
                $locale,
                ($missing -join ", "),
                ($unexpected -join ", "))
    }

    foreach ($key in $canonical.Keys) {
        $sourcePlaceholders = Get-PlaceholderSignature $canonical[$key]
        $translatedPlaceholders = Get-PlaceholderSignature $catalog[$key]
        if ($sourcePlaceholders -ne $translatedPlaceholders) {
            throw "Catalog $locale does not preserve placeholders for '$key'."
        }
    }
}

Write-Host (
    "Localization catalogs validated: {0} locales, {1} native keys, {2} code references." -f
        $expectedLocales.Count,
        $canonical.Count,
        $referencedKeys.Count)
