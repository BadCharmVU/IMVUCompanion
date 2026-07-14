; Inno Setup script — compile with ISCC.exe after running scripts/Publish-Release.ps1

#define AppVersion "0.7.1"
#ifndef PublishDir
#define PublishDir "..\publish"
#endif

[Setup]
AppId={{8F3C2A1B-9D4E-4F60-B1A2-C3D4E5F60701}
AppName=IMVU Companion
AppVersion={#AppVersion}
AppVerName=IMVU Companion v{#AppVersion}
AppPublisher=BadCharmVU
AppPublisherURL=https://github.com/BadCharmVU/IMVUCompanion
AppSupportURL=https://github.com/BadCharmVU/IMVUCompanion/issues
AppUpdatesURL=https://github.com/BadCharmVU/IMVUCompanion/releases
DefaultDirName={autopf}\IMVU Companion
DefaultGroupName=IMVU Companion
AllowNoIcons=yes
OutputDir=..\release
OutputBaseFilename=IMVUCompanion-Setup-v{#AppVersion}
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\IMVUCompanion.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=
InfoBeforeFile=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\IMVU Companion"; Filename: "{app}\IMVUCompanion.exe"; IconFilename: "{app}\IMVUCompanion.exe"
Name: "{group}\{cm:UninstallProgram,IMVU Companion}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\IMVU Companion"; Filename: "{app}\IMVUCompanion.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\IMVUCompanion.exe"; Description: "{cm:LaunchProgram,IMVU Companion}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\IMVUCompanion"