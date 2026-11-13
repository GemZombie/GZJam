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
; Song files open with GZJam (#336): Explorer is told the file types changed
ChangesAssociations=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; GZJam as a player for song files (#336): in Explorer's "Open with" for each kind it plays,
; per user (or for everyone, if installed so). It never takes over a default: Windows leaves
; that to the DJ ("Always", or Settings > Default apps).
Root: HKA; Subkey: "Software\Classes\GZJam.Song"; ValueType: string; ValueName: ""; ValueData: "Song"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\GZJam.Song\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\GZJam.exe,0"
Root: HKA; Subkey: "Software\Classes\GZJam.Song\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GZJam.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "GZJam"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GZJam.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".mp3"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".mp2"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".ogg"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".wav"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".aif"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".aiff"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".flac"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".opus"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".m4a"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".aac"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\GZJam.exe\SupportedTypes"; ValueType: string; ValueName: ".wma"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.mp2\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.aif\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.aiff\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.opus\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.wma\OpenWithProgids"; ValueType: string; ValueName: "GZJam.Song"; ValueData: ""; Flags: uninsdeletevalue

[Icons]
Name: "{autoprograms}\GZJam"; Filename: "{app}\GZJam.exe"
Name: "{autodesktop}\GZJam"; Filename: "{app}\GZJam.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GZJam.exe"; Description: "{cm:LaunchProgram,GZJam}"; Flags: nowait postinstall skipifsilent
