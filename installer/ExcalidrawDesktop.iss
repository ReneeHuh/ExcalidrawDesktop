; Build through build-installer.ps1 so identity, payload and prerequisite hashes agree.
#if Ver != EncodeVer(6, 7, 3)
  #error This build requires Inno Setup 6.7.3.
#endif
#ifndef AppVersion
  #error Run installer/build-installer.ps1 to supply the build parameters.
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Excalidraw Desktop contributors
DefaultDirName={localappdata}\Programs\ExcalidrawDesktop
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=ExcalidrawDesktop-Setup-{#AppVersion}-x64
SetupIconFile=..\ExcalidrawDesktop.App\Assets\Excalidraw.ico
UninstallDisplayIcon={app}\ExcalidrawDesktop.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
ChangesAssociations=yes
CloseApplications=no
RestartApplications=no
AppMutex={code:GetRunningMutexName}
SetupMutex=Global\ExcalidrawDesktop.Setup.{username}
AllowNoIcons=yes
#ifdef SigningEnabled
SignTool=DesktopSign
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebView2Installer}"; DestName: "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\ExcalidrawDesktop.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ExcalidrawDesktop.exe"; Tasks: desktopicon

; Register an Open with candidate, preserving the user's current default.
; Removal is conditional on the command still belonging to this installation.
[Registry]
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueData: "Excalidraw drawing"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\ExcalidrawDesktop.exe"",0"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\ExcalidrawDesktop.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\.excalidraw\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""
Root: HKCU; Subkey: "Software\ExcalidrawDesktop\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"
Root: HKCU; Subkey: "Software\ExcalidrawDesktop\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Draw and edit Excalidraw files"
Root: HKCU; Subkey: "Software\ExcalidrawDesktop\Capabilities\FileAssociations"; ValueType: string; ValueName: ".excalidraw"; ValueData: "{#ProgId}"
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "ExcalidrawDesktop"; ValueData: "Software\ExcalidrawDesktop\Capabilities"

[Run]
Filename: "{app}\ExcalidrawDesktop.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

#include "includes\WebView2.iss"
#include "includes\Installation.iss"
