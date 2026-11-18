#define AppVersion "0.5.4"
#define PublishDirectory "..\bin\publish-0.5.4-release"

[Setup]
AppId={{2F84D52E-4DB8-4B43-9CD7-19DE2FAE3C82}
AppName=SchoolDesk
AppVersion={#AppVersion}
AppPublisher=Xzolth-ADM
DefaultDirName={localappdata}\Programs\SchoolDesk
DefaultGroupName=SchoolDesk
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\bin
OutputBaseFilename=SchoolDesk-{#AppVersion}-Setup-win-x64
SetupIconFile=image_1_(1).ico
UninstallDisplayIcon={app}\SchoolDesk.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDirectory}\SchoolDesk.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Assets\schedule.json"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\SchoolDesk"; Filename: "{app}\SchoolDesk.exe"
Name: "{autodesktop}\SchoolDesk"; Filename: "{app}\SchoolDesk.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SchoolDesk.exe"; Description: "Launch SchoolDesk"; Flags: nowait postinstall skipifsilent
