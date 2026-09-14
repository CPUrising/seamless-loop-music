; Seamless Loop Music installer script (Inno Setup 6.3+).
;
; Usage (CI):
;   ISCC.exe /DAppVersion=1.11.0 SeamlessLoopMusic.iss
;   The app files are read from ..\artifacts\seamless-loop-music (staged by CI).
;
; Local build:
;   Copy the Release output into artifacts\seamless-loop-music first, or override:
;   ISCC.exe /DSourceDir="..\seamless loop music\bin\Release\net48" SeamlessLoopMusic.iss

#ifndef AppVersion
  #define AppVersion "1.11.0"
#endif

; SourceDir is intentionally relative so it never has to be passed on the
; command line (PowerShell mangles /D values that contain spaces).
#ifndef SourceDir
  #define SourceDir "..\artifacts\seamless-loop-music"
#endif

#ifndef AppIcon
  #define AppIcon "..\seamless loop music\Resources\app_icon.ico"
#endif

[Setup]
AppId={{E96BED78-EF4E-4630-8786-CF49AEC3725D}
AppName=Seamless Loop Music
AppVersion={#AppVersion}
AppVerName=Seamless Loop Music {#AppVersion}
AppPublisher=CPUrising
AppPublisherURL=https://github.com/CPurising/seamless-loop-music
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Seamless Loop Music
DefaultGroupName=Seamless Loop Music
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=SeamlessLoopMusic-Setup-{#AppVersion}
SetupIconFile={#AppIcon}
UninstallDisplayIcon={#AppIcon}
Compression=lzma2/ultra
SolidCompression=yes
CloseApplications=yes
SetupLogging=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Files]
; Ship program files only. The Data folder (user library DB) is intentionally
; excluded so updates preserve user data in place.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "Data,*.pdb,start_log.txt,crash_log.txt"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; No IconFilename is set so Windows uses the exe's own embedded icon,
; guaranteeing the shortcut icon matches the app icon.
Name: "{autoprograms}\Seamless Loop Music"; Filename: "{app}\seamless loop music.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Seamless Loop Music"; Filename: "{app}\seamless loop music.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Run]
; Launches the app after install (including silent updates) when invoked with /AUTOLAUNCH.
Filename: "{app}\seamless loop music.exe"; Description: "{cm:LaunchProgram,Seamless Loop Music}"; Flags: nowait; Check: AutoLaunch

[Code]
function AutoLaunch: Boolean;
begin
  Result := Pos('/AUTOLAUNCH', GetCmdTail()) > 0;
end;
