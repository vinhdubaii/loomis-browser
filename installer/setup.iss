; Loomis Browser — Inno Setup script
; Builds LoomisBrowser-Setup-{#MyAppVersion}.exe from the dotnet publish output.
;
; AppVersion is normally overridden at CI time via:
;   iscc setup.iss /DMyAppVersion=1.2.0
; so it stays in sync with the git tag, without editing this file by hand.

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#define MyAppName "Loomis Browser"
#define MyAppPublisher "Lidora Studio"
#define MyAppURL "https://github.com/vinhdubaii/loomis-browser"
#define MyAppExeName "LoomisBrowser.exe"

[Setup]
; This GUID must stay constant across every release so Windows/Inno Setup
; recognizes new installers as *updates* to the same app instead of a
; separate parallel install.
AppId={{8F2C6E1A-4B7D-4E2F-9A31-3C0B8D2F5E77}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\Loomis Browser
DefaultGroupName=Loomis Browser
DisableProgramGroupPage=yes
OutputBaseFilename=LoomisBrowser-Setup-{#MyAppVersion}
; SetupIconFile is optional while Assets/loomis.ico doesn't exist yet.
; Uncomment once the real icon is added:
; SetupIconFile=..\src\Assets\loomis.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; Everything from `dotnet publish` output gets dropped here by CI before
; compiling this script (see .github/workflows/release.yml). This includes
; the bundled Fixed Version WebView2 runtime folder ("WebView2\"), produced
; automatically by the WebView2.Runtime.X64 NuGet package — no separate
; [Files] entry is needed for it.
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Loomis Browser"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall Loomis Browser"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Loomis Browser"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Loomis Browser"; Flags: nowait postinstall skipifsilent
