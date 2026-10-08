; rexplayer - Windows installer (Inno Setup 6).
; Built by installer\build.ps1: ISCC /DAppVersion=<x.y.z> /DSource=<dist folder> rexplayer.iss
;
; Per-user install (no administrator prompt) into %LocalAppData%\Programs. The app keeps its own
; data in %LocalAppData%\REX\rexplayer; the uninstaller keeps it unless asked.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Source
  #define Source "..\dist"
#endif

#define AppName "rexplayer"
#define AppExe "rexplayer.exe"
#define CliExe "rexplay.exe"
#define HasApp FileExists(Source + "\rexplayer\" + AppExe)

[Setup]
; The AppId never changes: it is how an upgrade finds the installation it replaces.
AppId={{446DB6D6-2287-41A3-B8DB-661EF92AD360}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=REX Technologies
AppPublisherURL=https://tochi-mba.github.io/rexplayer/
AppSupportURL=https://github.com/tochi-mba/rexplayer/issues
AppUpdatesURL=https://github.com/tochi-mba/rexplayer/releases/latest
AppComments=Plays every file, stream and disc on Windows.
AppContact=https://github.com/tochi-mba/rexplayer/issues
VersionInfoDescription={#AppName} setup
VersionInfoProductName={#AppName}
VersionInfoCompany=REX Technologies
; A second installer would fight the first over the same files.
SetupMutex=RexplayerSetup
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputBaseFilename=rexplayer-Setup-{#AppVersion}
SetupIconFile=..\assets\rexplayer.ico
#if HasApp
UninstallDisplayIcon={app}\{#AppExe}
#else
UninstallDisplayIcon={app}\{#CliExe}
#endif
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SelectTasksLabel2=Choose how {#AppName} should be set up, then click Next.%n%nIt installs for you only, without administrator rights, and puts the rexplay command on your PATH.
FinishedHeadingLabel=Ready to play
FinishedLabelNoIcons={#AppName} is installed. Open any media file with it, or run rexplay in a terminal.
FinishedLabel={#AppName} is installed. Open any media file with it, or run rexplay in a terminal.
ConfirmUninstall=Remove {#AppName} from this PC?%n%nYour media files are not touched.

[Tasks]
#if HasApp
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "After installing:"; Flags: checkedonce
#endif
Name: "addtopath"; Description: "Add the rexplay command to my PATH"; GroupDescription: "After installing:"; Flags: checkedonce

[Files]
Source: "prepare-upgrade.ps1"; Flags: dontcopy
Source: "{#Source}\rexplayer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

#if HasApp
[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent
; Windows' own page for choosing defaults, opened on rexplayer: offered, never imposed.
Filename: "ms-settings:defaultapps?registeredAppUser={#AppName}"; Description: "Choose rexplayer as the default player (opens Windows Settings)"; Flags: shellexec nowait postinstall skipifsilent unchecked
; An update started from inside rexplayer installs silently, then opens it again where it left off.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: RelaunchAfterUpdate

; rexplayer in Explorer's "Open with" for every file it plays, and in Windows' Default apps.
#include "associations.iss"
#endif

[Code]
const
  EnvironmentKey = 'Environment';

// rexplayer passes /relaunch=1 when it installs an update of itself.
function RelaunchAfterUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:relaunch|0}') = '1');
end;

// Stop the installed copy before its files are replaced.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Arguments: String;
  ResultCode: Integer;
begin
  Result := '';
  ExtractTemporaryFile('prepare-upgrade.ps1');
  Arguments := ExpandConstant('-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{tmp}\prepare-upgrade.ps1" -InstallDirectory "{app}"');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Arguments, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := 'Could not start upgrade preparation. Close rexplayer and retry.';
  if (Result = '') and (ResultCode <> 0) then
    Result := 'Could not close the installed rexplayer. Close it and retry. No files have been replaced.';
end;

procedure EnvAddPath(Path: String);
var
  Paths: String;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    Paths := '';
  if Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';') > 0 then
    exit;
  if (Paths <> '') and (Paths[Length(Paths)] <> ';') then
    Paths := Paths + ';';
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths + Path);
end;

procedure EnvRemovePath(Path: String);
var
  Paths: String;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, EnvironmentKey, 'Path', Paths) then
    exit;
  P := Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';');
  if P = 0 then
    exit;
  Delete(Paths, P - 1, Length(Path) + 1);
  RegWriteExpandStringValue(HKCU, EnvironmentKey, 'Path', Paths);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    EnvAddPath(ExpandConstant('{app}'));
end;

// Settings, the library and logs outlive the app on purpose, so removing them is asked for rather
// than assumed, and never happens during a silent uninstall.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDirectory: String;
begin
  if CurUninstallStep <> usPostUninstall then
    exit;

  EnvRemovePath(ExpandConstant('{app}'));
  DataDirectory := ExpandConstant('{localappdata}\REX\{#AppName}');
  if not DirExists(DataDirectory) then
    exit;

  if UninstallSilent then
    exit;

  if MsgBox('Also delete your settings, library and logs?' + #13#10 + #13#10 +
      DataDirectory + #13#10 + #13#10 +
      'Keep them if you plan to install {#AppName} again.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataDirectory, True, True, True);
end;
