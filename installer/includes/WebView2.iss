[Code]
function HasWebView2Runtime: Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := (RegQueryStringValue(HKLM32, Key, 'pv', Version) and
    (Version <> '') and (Version <> '0.0.0.0'));
  if not Result then
    Result := (RegQueryStringValue(HKCU32, Key, 'pv', Version) and
      (Version <> '') and (Version <> '0.0.0.0'));
  if not Result then
    Result := (RegQueryStringValue(HKCU64, Key, 'pv', Version) and
      (Version <> '') and (Version <> '0.0.0.0'));
end;

function EnsureWebView2Runtime(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  InstallerPath: String;
begin
  Result := '';
  if HasWebView2Runtime then begin
    Log('WebView2 Runtime is already installed.');
    exit;
  end;
  WizardForm.StatusLabel.Caption := 'Installing Microsoft Edge WebView2 Runtime...';
  ExtractTemporaryFile('MicrosoftEdgeWebView2RuntimeInstallerX64.exe');
  InstallerPath := ExpandConstant('{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe');
  if CompareText(GetSHA256OfFile(InstallerPath), '{#WebView2SHA256}') <> 0 then begin
    Result := 'WebView2 installer verification failed. Download Excalidraw Desktop Setup again.';
    exit;
  end;
  if not Exec(InstallerPath, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := 'Microsoft Edge WebView2 Runtime could not be started. Please run Setup again.';
    exit;
  end;
  Log(Format('WebView2 installer exit code: %d', [ExitCode]));
  if ExitCode = 3010 then begin
    NeedsRestart := True;
    Result := 'Microsoft Edge WebView2 Runtime requires a restart. Restart Windows, then run Excalidraw Desktop Setup again.';
    exit;
  end;
  if ((ExitCode <> 0) and (ExitCode <> 3010)) or not HasWebView2Runtime then
    Result := Format('Microsoft Edge WebView2 Runtime installation did not complete (code %d). Restart Windows if requested, then run Setup again.', [ExitCode]);
end;
