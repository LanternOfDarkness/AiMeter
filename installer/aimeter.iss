; AiMeter installer (Inno Setup 6+).
;
; Build flow:
;   dotnet publish -c Release -r win-x64 --self-contained false -o publish\win-x64
;   iscc installer\aimeter.iss
; produces installer\Output\AiMeter-setup-x64.exe
;
; Per-user, no admin/UAC (PrivilegesRequired=lowest) - installs to
; %LocalAppData%\Programs\AiMeter, consistent with the app's existing per-user model
; (HKCU Run autorun, %AppData%\AiMeter settings, per-user WebView2 profile).

#define MyAppName "AiMeter"
#define MyAppPublisher "VeLab"
#define MyAppExeName "AiMeter.exe"
#define MyPublishDir "..\publish\win-x64"
#define MyAppVersion GetVersionNumbersString(MyPublishDir + "\" + MyAppExeName)

[Setup]
; Fixed AppId so upgrades are recognized as the same product across versions.
AppId={{8F3B7B7B-3B4B-4B9B-9B7B-1A2B3C4D5E6F}
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
OutputBaseFilename=AiMeter-setup-x64
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
; Best-effort prerequisite installs: skipped entirely if already present, and a failed
; download just shows a message rather than blocking the rest of setup.
Filename: "{tmp}\windowsdesktop-runtime-9-installer.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing .NET 9 Desktop Runtime..."; Check: NeedsDotNetRuntime; Flags: skipifdoesntexist waituntilterminated
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing WebView2 Runtime..."; Check: NeedsWebView2; Flags: skipifdoesntexist waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; No forced cleanup step here - CurUninstallStepChanged below asks before touching user data.

[Code]
var
  RemoveUserDataConfirmed: Boolean;

function RunHiddenAndCapture(const Exe, Params, OutFile: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Detects the .NET 9 Desktop Runtime via `dotnet --list-runtimes`, redirected to a temp
// file since Inno's Exec doesn't capture stdout directly.
function IsDotNetDesktopRuntimeInstalled(): Boolean;
var
  TmpFile: String;
  Output: TStringList;
  I: Integer;
begin
  Result := False;
  TmpFile := ExpandConstant('{tmp}\aimeter-dotnet-runtimes.txt');
  if RunHiddenAndCapture(ExpandConstant('{cmd}'),
       '/C dotnet --list-runtimes > "' + TmpFile + '" 2>&1', TmpFile) then
  begin
    if FileExists(TmpFile) then
    begin
      Output := TStringList.Create;
      try
        Output.LoadFromFile(TmpFile);
        for I := 0 to Output.Count - 1 do
        begin
          if Pos('Microsoft.WindowsDesktop.App 9.', Output[I]) > 0 then
          begin
            Result := True;
            Break;
          end;
        end;
      finally
        Output.Free;
      end;
    end;
  end;
end;

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

// Checked from [Run]'s Check parameter: returns True (so the [Run] entry fires) only when
// the runtime is missing AND the installer was downloaded successfully.
function NeedsDotNetRuntime(): Boolean;
begin
  Result := False;
  if IsDotNetDesktopRuntimeInstalled() then Exit;

  if DownloadWithPowerShell('https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe',
       ExpandConstant('{tmp}\windowsdesktop-runtime-9-installer.exe')) then
    Result := True
  else
    MsgBox('Could not download the .NET 9 Desktop Runtime automatically. Please install it ' +
      'manually from https://dotnet.microsoft.com/download/dotnet/9.0 after Setup finishes.',
      mbInformation, MB_OK);
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
