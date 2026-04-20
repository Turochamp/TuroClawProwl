; Inno Setup script for TuroClawProwl.
; Invoked by build.ps1 with /DMyAppVersion=<version>. Override when running iscc manually:
;   iscc /DMyAppVersion=0.1.0 TuroClawProwl.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName         "TuroClawProwl"
#define MyAppPublisher    "Creatus"
#define MyAppURL          "https://github.com/Turochamp/TuroClawProwl"
#define MyAppExeName      "TuroClawProwl.exe"

[Setup]
; Stable AppId — changing this breaks upgrade-in-place detection. Never regenerate.
AppId={{F5A3D8E2-9B4C-4A6F-8E1D-2C7B5A9E4F0D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}

; Per-user install: no UAC prompt, no admin required, SAC-friendlier.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=

DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
CreateAppDir=yes

; x64-only, matches the App project's Platforms.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Kill the running tray app during upgrade so the exe can be overwritten,
; then restart it if it was running.
CloseApplications=yes
RestartApplications=yes

OutputDir=dist
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "stage\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Ask the app to exit gracefully before files are removed. --exit is not a real
; flag — swallow failure silently; CloseApplications above is the real stop path.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--exit"; Flags: runhidden skipifdoesntexist; RunOnceId: "StopTuroClawProwl"

[UninstallDelete]
; App's own data dir (logs, config.json, token.dat) intentionally NOT deleted;
; preserves DPAPI-sealed token so reinstall Just Works. Remove manually from
; %APPDATA%\TuroClawProwl if you want a clean wipe.
