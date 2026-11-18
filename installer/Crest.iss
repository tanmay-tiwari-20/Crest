#ifndef SourceDir
  #define SourceDir "..\artifacts\portable\Crest"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifndef AppVersion
  #error "AppVersion must be supplied by scripts/package.ps1"
#endif
#ifndef AppFileVersion
  #error "AppFileVersion must be supplied by scripts/package.ps1"
#endif

[Setup]
AppId=Crest.PersonalUtility
AppName=Crest
AppVersion={#AppVersion}
AppVerName=Crest {#AppVersion}
AppPublisher=Crest
DefaultDirName={localappdata}\Programs\Crest
DefaultGroupName=Crest
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
WizardStyle=modern
SetupIconFile=..\assets\crest.ico
UninstallDisplayIcon={app}\Crest.exe
OutputDir={#OutputDir}
OutputBaseFilename=Crest-Setup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
TimeStampsInUTC=yes
CloseApplications=no
RestartApplications=no
SetupMutex=Local\Crest.Installer
InfoAfterFile=after-install.txt
VersionInfoDescription=Crest desktop utility setup
VersionInfoProductName=Crest
VersionInfoVersion={#AppFileVersion}
VersionInfoProductVersion={#AppVersion}
UninstallDisplayName=Crest

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Crest"; Filename: "{app}\Crest.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Crest.exe"
Name: "{autoprograms}\Crest Settings"; Filename: "{app}\Crest.exe"; Parameters: "--settings"; WorkingDir: "{app}"
Name: "{autodesktop}\Crest"; Filename: "{app}\Crest.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Crest.exe"; Description: "Start Crest in the system tray"; Flags: nowait postinstall skipifsilent

[Registry]
; No startup value is created. This only records cleanup of a value the user later enables in Settings.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Crest"; Flags: uninsdeletevalue

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
var
  DeleteLocalData: Boolean;

function OpenEvent(DesiredAccess: LongWord; InheritHandle: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SignalEvent(Handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function ReleaseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function StopCrest(): Boolean;
var
  EventHandle: THandle;
  Attempt: Integer;
begin
  Result := True;
  if not CheckForMutexes('Local\Crest.PersonalUtility') then exit;
  EventHandle := OpenEvent(2, False, 'Local\Crest.Quit');
  if EventHandle <> 0 then begin
    SignalEvent(EventHandle);
    ReleaseHandle(EventHandle);
    for Attempt := 1 to 100 do begin
      if not CheckForMutexes('Local\Crest.PersonalUtility') then exit;
      Sleep(50);
    end;
  end;
  Result := not CheckForMutexes('Local\Crest.PersonalUtility');
  if not Result then
    MsgBox('Crest is still running. Choose Quit from its tray menu and run setup again.', mbInformation, MB_OK);
end;

function InitializeSetup(): Boolean;
begin
  Result := StopCrest();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopCrest() then
    Result := 'Crest is still running. Quit it from the system tray before continuing.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := StopCrest();
  DeleteLocalData := False;
  if Result and not UninstallSilent then
    DeleteLocalData := MsgBox('Delete your saved Crest settings, shortcuts, timer state, and diagnostic data?' + #13#10 + #13#10 +
      'Choose No to keep them for a future installation. Original files in your shelf are never deleted.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if (CurUninstallStep = usPostUninstall) and DeleteLocalData then begin
    DataDir := ExpandConstant('{localappdata}\Crest');
    if (ExtractFileName(DataDir) = 'Crest') and
       (CompareText(ExtractFileDir(DataDir), ExpandConstant('{localappdata}')) = 0) then
      DelTree(DataDir, True, True, True);
  end;
end;
