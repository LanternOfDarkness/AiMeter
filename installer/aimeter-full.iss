; AiMeter installer - self-contained variant (Inno Setup 6+).
;
; This is the "full" build: the published payload is self-contained (~65-75 MB), so the
; .NET 10 Desktop Runtime prerequisite is NOT needed and has been removed below. The
; WebView2 Runtime is still required (both providers log in and fetch through it).
;
; Build flow:
;   dotnet publish src/AiMeter/AiMeter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\win-x64-full
;   iscc installer\aimeter-full.iss
; produces installer\Output\AiMeter-setup-full-x64.exe
;
; Per-user, no admin/UAC (PrivilegesRequired=lowest) - installs to
; %LocalAppData%\Programs\AiMeter, consistent with the app's existing per-user model
; (HKCU Run autorun, %AppData%\AiMeter settings, per-user WebView2 profile).

#define MyAppName "AiMeter"
#define MyAppPublisher "VeLab"
#define MyAppExeName "AiMeter.exe"
#define MyPublishDir "..\publish\win-x64-full"
#define MyAppVersion GetVersionNumbersString(MyPublishDir + "\" + MyAppExeName)

[Setup]
; Fixed AppId so upgrades are recognized as the same product across versions. Deliberately
; distinct from aimeter.iss so the framework-dependent and self-contained installers are
; treated as separate products (installing one does not upgrade/replace the other).
AppId={{A1C2D3E4-F5A6-4B7C-8D9E-0F1A2B3C4D5E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=AiMeter-setup-full-x64
SetupIconFile=..\src\AiMeter\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
; Both checked by default (Inno tasks are checked unless the "unchecked" flag is set) -
; matches the wizard flow's [x] Launch at startup / [x] Desktop shortcut defaults.
Name: "launchatstartup"; Description: "Launch AiMeter at Windows startup"; GroupDescription: "Additional tasks:"
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional tasks:"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Same HKCU Run key/value StartupManager (Services/StartupManager.cs) reads and writes, so
; the in-app Settings toggle and this installer checkbox always reflect the same value.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AiMeter"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: launchatstartup

[Run]
; Best-effort WebView2 prerequisite install: skipped entirely if already present, and a
; failed download just shows a message rather than blocking the rest of setup. No .NET
; Desktop Runtime step here - this payload is self-contained.
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing WebView2 Runtime..."; Check: NeedsWebView2; Flags: skipifdoesntexist waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; No forced cleanup step here - CurUninstallStepChanged below asks before touching user data.

[Code]
var
  RemoveUserDataConfirmed: Boolean;

function DownloadWithPowerShell(const Url, Dest: String): Boolean;
var
  ResultCode: Integer;
  Cmd: String;
begin
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-WebRequest -Uri ''' +
    Url + ''' -OutFile ''' + Dest + ''' -UseBasicParsing } catch { exit 1 }"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Cmd, '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) and FileExists(Dest);
end;

// WebView2 ships with Windows 11 and current Windows 10, so this is a fallback only -
// detected via the per-machine/per-user Client State registry key the Evergreen bootstrapper
// maintains.
function IsWebView2Installed(): Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKLM64, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKLM32, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) or
    RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version);
end;

function NeedsWebView2(): Boolean;
begin
  Result := False;
  if IsWebView2Installed() then Exit;

  if DownloadWithPowerShell('https://go.microsoft.com/fwlink/p/?LinkId=2124703',
       ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe')) then
    Result := True
  else
    MsgBox('Could not download the WebView2 Runtime automatically. Please install it manually ' +
      'from https://developer.microsoft.com/microsoft-edge/webview2/ after Setup finishes.',
      mbInformation, MB_OK);
end;

// Optional cleanup: settings/logs live in %AppData%\AiMeter, outside {app}, so the default
// uninstall leaves them untouched. Ask once, at the very start of uninstall, whether to
// remove them too - a plain confirmation dialog stands in for a dedicated uninstall-wizard
// checkbox page, which Inno's uninstaller doesn't support out of the box.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveUserDataConfirmed := MsgBox('Also remove AiMeter settings and logs (%AppData%\AiMeter)?',
      mbConfirmation, MB_YESNO) = IDYES;
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    if RemoveUserDataConfirmed then
    begin
      AppDataDir := ExpandConstant('{userappdata}\AiMeter');
      DelTree(AppDataDir, True, True, True);
    end;
  end;
end;
