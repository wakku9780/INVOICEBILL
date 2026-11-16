; Inno Setup 6 script for Saree Billing.
; Build with build-installer.ps1 (it publishes the app first and passes the version).
;
; Key decisions:
; - Installs for the current Windows user only (no administrator rights needed), into
;   %LOCALAPPDATA%\Programs\Saree Billing.
; - The business data lives in %LOCALAPPDATA%\SareeBilling (database, backups, logs).
;   This installer NEVER writes to or deletes that folder - not on update, reinstall
;   or uninstall - so invoices are never lost.
; - Self-contained: the .NET 8 runtime is inside the app folder.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "Saree Billing"
#define MyAppExeName "SareeBilling.exe"
#define PublishDir "..\artifacts\publish"

[Setup]
; Never change AppId: Windows uses it to recognise updates of the same app.
AppId={{24FC4472-5FB7-47D1-A0E6-06EAAACAD25C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=J A Sarees
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts\installer
OutputBaseFilename=SareeBilling-Setup-{#MyAppVersion}
SetupIconFile=..\src\SareeBilling.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; "Is the app still open?" is handled in [Code] below (it must wait for the app to close
; itself when the app starts an update). CloseApplications covers any other file locks.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; Remove the previous version's program files so no outdated DLL is left behind.
; Safe because the install folder is fixed (DisableDirPage) and holds only program files;
; business data is stored elsewhere.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
; In-app updates run setup with /SILENT: start the new version again when finished.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsSilentInstall

[Code]
const
  { Same name as the single-instance mutex created by the app (App.xaml.cs). }
  AppMutexName = 'SareeBilling.SingleInstance';

function IsSilentInstall(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if (CompareText(ParamStr(I), '/SILENT') = 0) or (CompareText(ParamStr(I), '/VERYSILENT') = 0) then
      Result := True;
end;

function InitializeSetup(): Boolean;
var
  Waited: Integer;
begin
  if IsSilentInstall() then
  begin
    { Started by the app's "Update now": the app is closing itself; give it up to 30 seconds. }
    Waited := 0;
    while CheckForMutexes(AppMutexName) and (Waited < 30000) do
    begin
      Sleep(500);
      Waited := Waited + 500;
    end;
    { Still open: do not touch anything. The app reports the unfinished update next time. }
    Result := not CheckForMutexes(AppMutexName);
    exit;
  end;

  { Normal double-click install: ask the user to close the app first. }
  while CheckForMutexes(AppMutexName) do
  begin
    if MsgBox('Saree Billing is open. Please close it, then click OK to continue.', mbInformation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
      exit;
    end;
  end;
  Result := True;
end;

[Messages]
FinishedLabel=Saree Billing has been installed.%n%nYour invoices and customers are stored separately and are kept safe when the app is updated or uninstalled.
