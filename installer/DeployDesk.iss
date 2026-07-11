#define AppName "DeployDesk"
#define AppVersion "0.2.0"
#define AppExeName "DeployDesk.exe"

[Setup]
AppId={{4137F89D-35A1-4E46-B836-D044CFBE563A}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=DeployDesk-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
WizardStyle=modern
SetupIconFile=..\src\DeployDesk\Assets\DeployDesk.ico
UninstallDisplayIcon={app}\{#AppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\artifacts\publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Symbole:"

[Registry]
Root: HKCU; Subkey: "Software\Classes\.deploylink"; ValueType: string; ValueData: "DeployDesk.Project"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\DeployDesk.Project"; ValueType: string; ValueData: "DeployDesk-Projekt"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\DeployDesk.Project\DefaultIcon"; ValueType: string; ValueData: "{app}\{#AppExeName},0"
Root: HKCU; Subkey: "Software\Classes\DeployDesk.Project\shell\open\command"; ValueType: string; ValueData: """{app}\{#AppExeName}"" ""%1"""

[Run]
Filename: "{app}\{#AppExeName}"; Description: "DeployDesk starten"; Flags: nowait postinstall skipifsilent
