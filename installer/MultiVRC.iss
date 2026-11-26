#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion AppVersion
#endif
#ifndef PublishDir
  #define PublishDir "out\publish"
#endif

[Setup]
AppId={{6B0F3E52-9C4A-4E0B-9D61-6F2E8A3C7B15}
AppName=MultiVRC
AppVersion={#AppVersion}
AppVerName=MultiVRC {#AppVersion}
AppPublisher=MultiVRC
VersionInfoVersion={#NumericVersion}
PrivilegesRequired=lowest
DefaultDirName={autopf}\MultiVRC
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\src\MultiVRC.App\icon\MultiVRC.ico
UninstallDisplayIcon={app}\MultiVRC.exe
UninstallDisplayName=MultiVRC
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=out
OutputBaseFilename=MultiVRC-Setup-{#AppVersion}
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\MultiVRC"; Filename: "{app}\MultiVRC.exe"
Name: "{autodesktop}\MultiVRC"; Filename: "{app}\MultiVRC.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\multivrc"; ValueType: string; ValueName: ""; ValueData: "URL:MultiVRC"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\multivrc"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\multivrc\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\MultiVRC.exe"",0"
Root: HKA; Subkey: "Software\Classes\multivrc\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MultiVRC.exe"" ""%1"""

[Run]
Filename: "{app}\MultiVRC.exe"; Description: "Start MultiVRC"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Data := ExpandConstant('{localappdata}\MultiVRC');
  if not DirExists(Data) then Exit;
  if UninstallSilent then Exit;
  if MsgBox('Also delete MultiVRC''s data?' + #13#10 + #13#10 +
            'Your instances, the VRChat builds it downloaded and its settings, in:' + #13#10 + Data + #13#10 + #13#10 +
            'Choose No to keep them for a later reinstall.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(Data, True, True, True);
end;
