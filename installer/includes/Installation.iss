[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1';
var
  PreviousFiles: TArrayOfString;
  MaintenanceMutex: THandle;

function GetFileAttributes(FileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function NativeCreateMutex(Attributes: Integer; InitialOwner: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function NativeWaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function NativeReleaseMutex(Handle: THandle): Boolean;
  external 'ReleaseMutex@kernel32.dll stdcall';
function NativeCloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

procedure EndMaintenance;
begin
  if MaintenanceMutex <> 0 then begin
    NativeReleaseMutex(MaintenanceMutex);
    NativeCloseHandle(MaintenanceMutex);
    MaintenanceMutex := 0;
  end;
end;

function BeginMaintenance: Boolean;
var
  Status: LongWord;
begin
  if MaintenanceMutex <> 0 then begin Result := True; exit; end;
  MaintenanceMutex := NativeCreateMutex(0, False, '{#MaintenanceMutexPrefix}' +
    Lowercase(GetSHA256OfUnicodeString(ExpandConstant('{localappdata}'))));
  Result := False;
  if MaintenanceMutex = 0 then exit;
  Status := NativeWaitForSingleObject(MaintenanceMutex, 0);
  Result := (Status = 0) or (Status = $80);
  if not Result then begin
    NativeCloseHandle(MaintenanceMutex);
    MaintenanceMutex := 0;
  end;
end;

function GetRunningMutexName(Param: String): String;
begin
  Result := '{#MutexPrefix}' + Lowercase(GetSHA256OfUnicodeString(ExpandConstant('{localappdata}')));
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if WizardSilent and CheckForMutexes(GetRunningMutexName('')) then begin
    Log('Setup blocked: Excalidraw Desktop is running. Close it and retry.');
    Result := False;
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := BeginMaintenance;
  if not Result then begin
    SuppressibleMsgBox('Another Excalidraw Desktop installation is in progress. Try again when it finishes.', mbError, MB_OK, IDOK);
    exit;
  end;
  if CheckForMutexes(GetRunningMutexName('')) then begin
    Log('Uninstall blocked: Excalidraw Desktop is running. Close it and retry.');
    SuppressibleMsgBox('Save your drawings and close Excalidraw Desktop before uninstalling.', mbError, MB_OK, IDOK);
    EndMaintenance;
    Result := False;
  end;
end;

function IsWithin(Path, Parent: String): Boolean;
begin
  Result := CompareText(Copy(AddBackslash(Path), 1, Length(AddBackslash(Parent))),
    AddBackslash(Parent)) = 0;
end;

function HasReparseComponent(Path: String): Boolean;
var
  Attributes: LongWord;
  Parent: String;
begin
  Result := False;
  while Length(Path) > 3 do begin
    Attributes := GetFileAttributes(Path);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then begin
      Result := True;
      exit;
    end;
    Parent := ExtractFileDir(Path);
    if Parent = Path then exit;
    Path := Parent;
  end;
end;

function IsNonEmptyDirectory(Path: String): Boolean;
var
  Entry: TFindRec;
begin
  Result := False;
  if FindFirst(AddBackslash(Path) + '*', Entry) then begin
    try
      repeat
        if (Entry.Name <> '.') and (Entry.Name <> '..') then begin
          Result := True;
          exit;
        end;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

function IsNewerVersion(ExistingVersion, IncomingVersion: Int64): Boolean;
var
  A, B, C, D, X, Y, Z, W: Word;
begin
  { ComparePackedVersion in Inno 6.7.3 compares signed Int64 values, which
    orders major versions >= 32768 below zero. Compare unsigned components. }
  UnpackVersionComponents(ExistingVersion, A, B, C, D);
  UnpackVersionComponents(IncomingVersion, X, Y, Z, W);
  if A <> X then Result := A > X
  else if B <> Y then Result := B > Y
  else if C <> Z then Result := C > Z
  else Result := D > W;
end;

function ValidateAndPrepareInstall(var NeedsRestart: Boolean): String;
var
  Directory, RegisteredDirectory, RegisteredVersion, DataDirectory: String;
  Marker: AnsiString;
  ExistingVersion, IncomingVersion: Int64;
begin
  Result := '';
  SetArrayLength(PreviousFiles, 0);
  if CheckForMutexes(GetRunningMutexName('')) then begin
    Result := 'Close Excalidraw Desktop before installing. Save your drawings, then retry.';
    exit;
  end;
  Directory := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  DataDirectory := ExpandConstant('{localappdata}\ExcalidrawDesktop');
  if IsWithin(Directory, DataDirectory) or IsWithin(DataDirectory, Directory) then begin
    Result := 'Choose a separate application folder. The selected folder overlaps Excalidraw Desktop user data.';
    exit;
  end;
  DataDirectory := GetEnv('EXCALIDRAW_DESKTOP_DATA_ROOT');
  if (DataDirectory <> '') and
    (IsWithin(Directory, DataDirectory) or IsWithin(DataDirectory, Directory)) then begin
    Result := 'Choose a folder separate from EXCALIDRAW_DESKTOP_DATA_ROOT.';
    exit;
  end;
  if HasReparseComponent(Directory) then begin
    Result := 'Choose a local installation folder without symbolic links or junctions.';
    exit;
  end;
  if RegQueryStringValue(HKCU64, UninstallKey, 'InstallLocation', RegisteredDirectory) and
    (CompareText(RemoveBackslashUnlessRoot(RegisteredDirectory), Directory) <> 0) then begin
    Result := 'An installation already exists in ' + RegisteredDirectory +
      '. Upgrade in that folder, or uninstall it before choosing another folder.';
    exit;
  end;
  if IsNonEmptyDirectory(Directory) then begin
    if not LoadStringFromFile(AddBackslash(Directory) + '{#MarkerFile}', Marker) or
      (Trim(String(Marker)) <> '{#AppId}') then begin
      Result := 'The selected folder contains files from outside this installer. Choose an empty folder.';
      exit;
    end;
  end;
  StrToVersion('{#AppVersion}', IncomingVersion);
  if (RegQueryStringValue(HKCU64, UninstallKey, 'DisplayVersion', RegisteredVersion) and
      StrToVersion(RegisteredVersion, ExistingVersion)) then begin
    if IsNewerVersion(ExistingVersion, IncomingVersion) then begin
      Result := 'A newer version is installed. Uninstall it before installing an older version. Your user data will be preserved.';
      exit;
    end;
  end;
  if GetPackedVersion(AddBackslash(Directory) + 'ExcalidrawDesktop.exe', ExistingVersion) then begin
    if IsNewerVersion(ExistingVersion, IncomingVersion) then begin
      Result := 'The selected folder contains a newer version. Downgrades require uninstalling first.';
      exit;
    end;
  end;
  LoadStringsFromFile(AddBackslash(Directory) + 'installed-files.txt', PreviousFiles);
  Result := EnsureWebView2Runtime(NeedsRestart);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if not BeginMaintenance then begin
    Result := 'Another Excalidraw Desktop installation is in progress. Try again when it finishes.';
    exit;
  end;
  Result := ValidateAndPrepareInstall(NeedsRestart);
  if Result <> '' then EndMaintenance;
end;

procedure DeinitializeSetup;
begin
  EndMaintenance;
end;

procedure DeinitializeUninstall;
begin
  EndMaintenance;
end;

function IsSafePayloadFile(RelativePath: String): Boolean;
var
  Extension: String;
begin
  Extension := Lowercase(ExtractFileExt(RelativePath));
  Result := (RelativePath <> '') and (Pos(':', RelativePath) = 0) and
    (Pos('..', RelativePath) = 0) and (Pos('/', RelativePath) = 0) and
    (Copy(RelativePath, 1, 1) <> '\') and
    (Pos('\UsageLogs\', '\' + RelativePath) = 0) and
    (Pos('\CrashLogs\', '\' + RelativePath) = 0) and
    (Pos('\WebView2\', '\' + RelativePath) = 0) and
    (Extension <> '.excalidraw') and (Extension <> '.excalidrawlib') and
    (CompareText(RelativePath, 'settings.json') <> 0) and
    (CompareText(RelativePath, 'workspace-state.json') <> 0) and
    (Pos('unins', Lowercase(ExtractFileName(RelativePath))) <> 1);
end;

procedure RemoveObsoletePayloadFiles;
var
  CurrentFiles: TArrayOfString;
  I, J: Integer;
  Found: Boolean;
  Path: String;
begin
  if not LoadStringsFromFile(ExpandConstant('{app}\installed-files.txt'), CurrentFiles) then exit;
  for I := 0 to GetArrayLength(PreviousFiles) - 1 do begin
    Found := False;
    for J := 0 to GetArrayLength(CurrentFiles) - 1 do
      if CompareText(PreviousFiles[I], CurrentFiles[J]) = 0 then Found := True;
    if not Found and IsSafePayloadFile(PreviousFiles[I]) then begin
      Path := ExpandConstant('{app}\') + PreviousFiles[I];
      if not HasReparseComponent(Path) and FileExists(Path) then begin
        if DeleteFile(Path) then Log('Removed obsolete application file: ' + PreviousFiles[I])
        else Log('Could not remove obsolete application file: ' + PreviousFiles[I]);
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    RemoveObsoletePayloadFiles;
    EndMaintenance;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command, Capabilities: String;
begin
  if CurUninstallStep <> usUninstall then exit;
  if RegQueryStringValue(HKCU64, 'Software\Classes\{#ProgId}\shell\open\command', '', Command) and
    (CompareText(Command, ExpandConstant('"{app}\ExcalidrawDesktop.exe" "%1"')) = 0) then begin
    RegDeleteKeyIncludingSubkeys(HKCU64, 'Software\Classes\{#ProgId}');
    RegDeleteValue(HKCU64, 'Software\Classes\.excalidraw\OpenWithProgids', '{#ProgId}');
    RegDeleteKeyIncludingSubkeys(HKCU64, 'Software\ExcalidrawDesktop\Capabilities');
    if RegQueryStringValue(HKCU64, 'Software\RegisteredApplications', 'ExcalidrawDesktop', Capabilities) and
      (CompareText(Capabilities, 'Software\ExcalidrawDesktop\Capabilities') = 0) then
      RegDeleteValue(HKCU64, 'Software\RegisteredApplications', 'ExcalidrawDesktop');
  end;
end;
