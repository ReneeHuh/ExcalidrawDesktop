#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $InstallerPath)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'SmokeTestCommon.ps1')
Add-Type -AssemblyName UIAutomationClient
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$identity = Get-Content (Join-Path $repositoryRoot 'installer/identity.json') -Raw | ConvertFrom-Json
$registryRoot = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
    [Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryView]::Registry64)
$uninstallKey = "Software\Microsoft\Windows\CurrentVersion\Uninstall\$($identity.appId)_is1"
$progIdKey = "Software\Classes\$($identity.progId)"
$reportRoot = Join-Path $repositoryRoot ('artifacts/installer-tests/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('excalidraw-installer-test-' + [guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $testRoot ('Installed App ' + [char]0x65E5 + [char]0x672C)
$dataRoot = Join-Path $testRoot 'user-data'
$testProcess = $null
$installationCreated = $false
$checks = [Collections.Generic.List[string]]::new()

function Read-RegistryValue([string] $Key, [string] $Name = '') {
    $opened = $registryRoot.OpenSubKey($Key)
    if (-not $opened) { return $null }
    try { return $opened.GetValue($Name, $null) } finally { $opened.Dispose() }
}

function Get-DefaultAssociation {
    [ordered]@{
        extension = Read-RegistryValue 'Software\Classes\.excalidraw'
        userChoice = Read-RegistryValue 'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.excalidraw\UserChoice' 'ProgId'
        userChoiceHash = Read-RegistryValue 'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.excalidraw\UserChoice' 'Hash'
    } | ConvertTo-Json -Compress
}

function Invoke-Setup([string] $Name, [string] $Directory = $installRoot, [switch] $ExpectFailure) {
    $process = Start-Process -FilePath $InstallerPath -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        ('/DIR="{0}"' -f $Directory), ('/LOG="{0}"' -f (Join-Path $reportRoot "$Name.log"))
    ) -WindowStyle Hidden -PassThru -Wait
    if ($ExpectFailure -and $process.ExitCode -eq 0) { throw "$Name unexpectedly succeeded." }
    if (-not $ExpectFailure -and $process.ExitCode -ne 0) { throw "$Name failed with code $($process.ExitCode). See $reportRoot." }
    $checks.Add("$Name (exit $($process.ExitCode))")
}

function Invoke-Uninstall([string] $Name, [switch] $ExpectFailure) {
    $process = Start-Process -FilePath (Join-Path $installRoot 'unins000.exe') -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        ('/LOG="{0}"' -f (Join-Path $reportRoot "$Name.log"))
    ) -WindowStyle Hidden -PassThru -Wait
    if ($ExpectFailure -and $process.ExitCode -eq 0) { throw "$Name unexpectedly succeeded." }
    if (-not $ExpectFailure -and $process.ExitCode -ne 0) { throw "$Name failed with code $($process.ExitCode)." }
    $checks.Add("$Name (exit $($process.ExitCode))")
}

try {
    # This test temporarily owns the actual per-user product registration. Refuse
    # an existing installation or association instead of backing over user state.
    foreach ($key in @($uninstallKey, $progIdKey, 'Software\ExcalidrawDesktop\Capabilities')) {
        $existing = $registryRoot.OpenSubKey($key)
        if ($existing) { $existing.Dispose(); throw "Existing installation/registration found at HKCU\$key. Use a clean test user." }
    }
    if ((Read-RegistryValue 'Software\RegisteredApplications' 'ExcalidrawDesktop') -or
        $null -ne (Read-RegistryValue 'Software\Classes\.excalidraw\OpenWithProgids' $identity.progId)) {
        throw 'An existing Excalidraw Desktop registration is present. Use a clean test user.'
    }
    if (@(Get-Process -Name ExcalidrawDesktop -ErrorAction SilentlyContinue).Count) {
        throw 'Close Excalidraw Desktop before running installer tests.'
    }
    $defaultBefore = Get-DefaultAssociation
    $null = New-Item -ItemType Directory -Path $reportRoot, $dataRoot -Force
    [IO.File]::WriteAllText((Join-Path $dataRoot 'drawing.excalidraw'), 'user-data-sentinel')
    $foreignRoot = Join-Path $testRoot 'existing-files'
    $null = New-Item -ItemType Directory -Path $foreignRoot
    [IO.File]::WriteAllText((Join-Path $foreignRoot 'drawing.excalidraw'), 'keep-me')
    Invoke-Setup 'reject-nonempty-folder' -Directory $foreignRoot -ExpectFailure
    Invoke-Setup 'fresh-install'
    $installationCreated = $true
    $executable = Join-Path $installRoot 'ExcalidrawDesktop.exe'
    $marker = [IO.File]::ReadAllText((Join-Path $installRoot $identity.markerFile)).Trim()
    if ($marker -ne $identity.appId) { throw 'Wrong installation marker.' }
    $command = Read-RegistryValue "$progIdKey\shell\open\command"
    if ($command -ne ('"{0}" "%1"' -f $executable)) { throw "Wrong open command: $command" }
    if ((Get-DefaultAssociation) -ne $defaultBefore) { throw 'Setup changed the default association.' }
    foreach ($file in @('LICENSE', 'THIRD_PARTY_NOTICES.md', 'ExcalidrawDesktop.pri', 'App.xbf', 'coreclr.dll', 'Assets/Web/index.html')) {
        if (-not (Test-Path -LiteralPath (Join-Path $installRoot $file))) { throw "Missing installed asset: $file" }
    }
    $checks.Add('Installed assets, identity, quoted file association, default preserved')

    $localData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $suffix = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::Unicode.GetBytes($localData))).ToLowerInvariant()
    $maintenance = [Threading.Mutex]::new($false, $identity.maintenanceMutexPrefix + $suffix)
    if (-not $maintenance.WaitOne(1000)) { throw 'Could not acquire isolated maintenance test gate.' }
    try {
        $blockedApplication = [pscustomobject]@{ InstallLocation = $installRoot; ExecutablePath = $executable; DataRoot = $dataRoot }
        $testProcess = Start-DesktopTestApplication -Application $blockedApplication
        if (-not $testProcess.WaitForExit(10000) -or $testProcess.ExitCode -ne 0) {
            throw 'Application startup did not defer during maintenance.'
        }
        $testProcess = $null
        Invoke-Setup 'reject-concurrent-maintenance' -ExpectFailure
        Invoke-Uninstall 'reject-uninstall-during-maintenance' -ExpectFailure
    }
    finally { $maintenance.ReleaseMutex(); $maintenance.Dispose() }
    $checks.Add('Maintenance gate blocks new app startup and concurrent setup/uninstall')

    # Launch in normal mode with isolated data: this checks that an installed app
    # does not perform portable registration or create a registration stamp.
    $oldData = $env:EXCALIDRAW_DESKTOP_DATA_ROOT
    $oldSmoke = $env:EXCALIDRAW_DESKTOP_SMOKE_TEST
    try {
        $env:EXCALIDRAW_DESKTOP_DATA_ROOT = $dataRoot
        $env:EXCALIDRAW_DESKTOP_SMOKE_TEST = $null
        $testProcess = Start-Process -FilePath $executable -WorkingDirectory $installRoot -WindowStyle Hidden -PassThru
    }
    finally { $env:EXCALIDRAW_DESKTOP_DATA_ROOT = $oldData; $env:EXCALIDRAW_DESKTOP_SMOKE_TEST = $oldSmoke }
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $testProcess.Refresh()
        if ($testProcess.HasExited) { throw 'Installed app exited during startup.' }
        if ($testProcess.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($testProcess.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Installed app did not open a window.' }
    $uiRoot = [System.Windows.Automation.AutomationElement]::FromHandle($testProcess.MainWindowHandle)
    $editorCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ExcalidrawEditorWebView')
    $startupCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'StartupStatusText')
    $editorReady = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $editor = $uiRoot.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editorCondition)
        $startup = $uiRoot.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $startupCondition)
        if ($editor -and -not $editor.Current.IsOffscreen -and (-not $startup -or $startup.Current.IsOffscreen)) {
            $editorReady = $true
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $editorReady) { throw 'Installed editor did not finish loading its local web assets and bridge.' }
    $checks.Add('Installed WebView2 editor reached ready state')
    if (Test-Path -LiteralPath (Join-Path $dataRoot 'file-association.stamp')) { throw 'Installed app performed portable registration.' }
    Invoke-Setup 'reject-upgrade-while-running' -ExpectFailure
    Invoke-Uninstall 'reject-uninstall-while-running' -ExpectFailure
    $testProcess.Refresh()
    if ($testProcess.HasExited) { throw 'Setup or uninstall closed the running app.' }
    $null = $testProcess.CloseMainWindow()
    if (-not $testProcess.WaitForExit(15000)) { throw 'Installed app did not close normally.' }
    $testProcess = $null
    $checks.Add('Normal installed startup and graceful exit')

    $application = [pscustomobject]@{ InstallLocation = $installRoot; ExecutablePath = $executable }
    & (Join-Path $PSScriptRoot 'Invoke-FileActivationSmokeTest.ps1') -SkipBuild -Application $application |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportRoot 'activation.json')
    $checks.Add('Installed Release activation: two files, Unicode/spaces, duplicate focus, single instance')

    # Simulate a payload file removed by a newer release, and a user-created file.
    [IO.File]::WriteAllText((Join-Path $installRoot 'obsolete-test.dll'), 'installer-owned')
    [IO.File]::AppendAllText((Join-Path $installRoot 'installed-files.txt'), "obsolete-test.dll`r`n")
    [IO.File]::WriteAllText((Join-Path $installRoot 'personal-note.txt'), 'keep-me')
    Invoke-Setup 'repair-and-obsolete-cleanup'
    if (Test-Path -LiteralPath (Join-Path $installRoot 'obsolete-test.dll')) { throw 'Obsolete payload was not removed.' }
    if ([IO.File]::ReadAllText((Join-Path $installRoot 'personal-note.txt')) -ne 'keep-me') { throw 'Unowned file changed.' }
    $installedVersion = Read-RegistryValue $uninstallKey 'DisplayVersion'
    $key = $registryRoot.OpenSubKey($uninstallKey, $true)
    try { $key.SetValue('DisplayVersion', '65534.0.0') }
    finally { $key.Dispose() }
    try {
        Invoke-Setup 'reject-downgrade' -ExpectFailure
    }
    finally {
        # Reopen: a broken downgrade guard may have recreated this registry key.
        $key = $registryRoot.OpenSubKey($uninstallKey, $true)
        if ($key) {
            try { $key.SetValue('DisplayVersion', $installedVersion) }
            finally { $key.Dispose() }
        }
    }
    Invoke-Uninstall 'uninstall'
    $installationCreated = $false
    foreach ($keyName in @($uninstallKey, $progIdKey, 'Software\ExcalidrawDesktop\Capabilities')) {
        $key = $registryRoot.OpenSubKey($keyName)
        if ($key) { $key.Dispose(); throw "Uninstall left its registry key: $keyName" }
    }
    if (Test-Path -LiteralPath $executable) { throw 'Uninstall left the executable.' }
    if ((Get-DefaultAssociation) -ne $defaultBefore) { throw 'Uninstall changed the default association.' }
    if ([IO.File]::ReadAllText((Join-Path $dataRoot 'drawing.excalidraw')) -ne 'user-data-sentinel' -or
        [IO.File]::ReadAllText((Join-Path $installRoot 'personal-note.txt')) -ne 'keep-me') { throw 'Uninstall changed user files.' }
    $checks.Add('Uninstall preserved user data, unowned files and default association')
    [ordered]@{ result = 'Passed'; installer = $InstallerPath; checks = @($checks) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportRoot 'results.json')
    Write-Host "Installer smoke tests passed. Logs: $reportRoot"
}
finally {
    Stop-DesktopTestProcess $testProcess
    $ownedMarker = Join-Path $installRoot $identity.markerFile
    $registeredRoot = Read-RegistryValue $uninstallKey 'InstallLocation'
    $ownsRegistration = $registeredRoot -and
        $registeredRoot.TrimEnd('\') -eq $installRoot.TrimEnd('\') -and
        (Test-Path -LiteralPath $ownedMarker) -and
        [IO.File]::ReadAllText($ownedMarker).Trim() -eq $identity.appId
    if (($installationCreated -or $ownsRegistration) -and (Test-Path -LiteralPath (Join-Path $installRoot 'unins000.exe'))) {
        Invoke-Uninstall 'cleanup-uninstall'
    }
    $registryRoot.Dispose()
    # Preserve failed runs for diagnosis. Successful runs leave only report logs.
    if (Test-Path -LiteralPath (Join-Path $reportRoot 'results.json')) {
        $resolved = [IO.Path]::GetFullPath($testRoot)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if (-not $resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolved) -notlike 'excalidraw-installer-test-*') { throw 'Unsafe test cleanup path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
