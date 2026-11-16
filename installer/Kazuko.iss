#ifndef AppVersion
  #error Build using scripts/Build-Setup.ps1
#endif

[Setup]
AppId={{40C52ECA-8F30-4596-9B1B-C254017743DE}
AppName=KAZUKO ERP JEWELRY
AppVersion={#AppVersion}
AppPublisher=KAZUKO
DefaultDirName={localappdata}\Programs\Kazuko ERP
DefaultGroupName=KAZUKO ERP
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.18362
OutputDir={#OutputDir}
OutputBaseFilename=KazukoERP-Setup-{#AppVersion}-win-x64
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\KAZUKO_ERP_JEWELRY.exe

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WebViewInstaller}"; DestName: "WebView2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{group}\KAZUKO ERP"; Filename: "{app}\KAZUKO_ERP_JEWELRY.exe"; WorkingDir: "{app}"
Name: "{group}\Installation guide"; Filename: "{app}\INSTALLATION.md"
Name: "{autodesktop}\KAZUKO ERP"; Filename: "{app}\KAZUKO_ERP_JEWELRY.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{app}\Start-Kazuko.ps1"
Type: files; Name: "{app}\Configure-Database.ps1"
Type: files; Name: "{group}\Configure database.lnk"

[Run]
Filename: "{app}\KAZUKO_ERP_JEWELRY.exe"; Description: "Open KAZUKO ERP"; Flags: postinstall nowait skipifsilent

[Code]
function HasWebView2: Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := RegQueryStringValue(HKCU, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
  if not Result then
    Result := RegQueryStringValue(HKLM32, Key, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if HasWebView2 then exit;
  ExtractTemporaryFile('WebView2Setup.exe');
  if not Exec(ExpandConstant('{tmp}\WebView2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := 'Cannot start WebView2 installer.'
  else if not HasWebView2 then
    Result := 'WebView2 installation failed (code ' + IntToStr(ResultCode) + '). Check internet access or use an offline WebView2 installer.';
end;
