; TuroClawProwl installer
; Usage: iscc installer\installer.iss
; Assumes `dotnet publish -c Release -r win-x64 --self-contained true -o publish/` has run.

#define AppId "{{E4B6E9F5-3E24-4D7F-8DD6-7F1C8E4F0A11}}"
#define AppName "TuroClawProwl"
#define AppVersion "0.3.0"
#define AppPublisher "Creatus"
#define AppExeName "TuroClawProwl.exe"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename=TuroClawProwlSetup-{#AppVersion}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
UsePreviousAppDir=yes

[Files]
; Pulls in the self-contained publish output (all .dll + .exe + runtime).
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "Launch {#AppName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[UninstallDelete]
; Remove logs but preserve %APPDATA%\TuroClawProwl\config.json per §7.
Type: filesandordirs; Name: "{userappdata}\TuroClawProwl\logs"

; Notes:
; * Autostart is written/removed at runtime by the app (DD-8), not here.
; * Start Menu shortcut also serves as the toast AUMID host for the
;   Microsoft.Toolkit.Uwp.Notifications adapter (see DD-2 amendment).
