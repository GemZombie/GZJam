; Windows installer for GZJam (Inno Setup 6). Built by .github/workflows/release.yml:
;   iscc /DVersion=1.2.0 /DSourceDir=<publish folder> /DOutputDir=<dir> packaging\windows\GZJam.iss
;
; Installs for the current user (no administrator prompt) into %LOCALAPPDATA%\Programs\GZJam.
; Uninstalling removes the program only: settings, the library database and the DJ's data in
; %APPDATA%\GZJam and %LOCALAPPDATA%\GZJam are left alone, and music files are never touched.

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\publish"
#endif

[Setup]
AppId={{6D3F3C2A-8E54-4B1F-9C6E-2A7F0B9D4E11}
AppName=GZJam
AppVersion={#Version}
AppVerName=GZJam {#Version}
AppPublisher=GemZombie
DefaultDirName={autopf}\GZJam
DefaultGroupName=GZJam
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=GZJam-{#Version}-windows-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\GZJam.exe
SetupIconFile=..\..\src\GZJam.App\Assets\gzjam.ico
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\GZJam"; Filename: "{app}\GZJam.exe"
Name: "{autodesktop}\GZJam"; Filename: "{app}\GZJam.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GZJam.exe"; Description: "{cm:LaunchProgram,GZJam}"; Flags: nowait postinstall skipifsilent
