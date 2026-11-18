; Per-user installer for Pods. Build with installer\build.ps1.
; Major.Minor.Patch from Pods.exe, which takes <Version> from Pods.App.csproj.
#define Major
#define Minor
#define Patch
#define Build
#expr GetVersionComponents(AddBackslash(SourcePath) + "..\artifacts\publish\Pods.exe", Major, Minor, Patch, Build)
#define AppVersion Str(Major) + "." + Str(Minor) + "." + Str(Patch)

[Setup]
AppId={{6F0B7C52-4E8A-4C1B-9F3E-2B6D3A1E7C44}
AppName=Pods
AppVersion={#AppVersion}
AppPublisher=nullopt
DefaultDirName={localappdata}\Programs\Pods
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts
OutputBaseFilename=PodsSetup-{#AppVersion}
SetupIconFile=..\src\Pods.App\Assets\Pods.ico
UninstallDisplayIcon={app}\Pods.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force

[Tasks]
Name: startup; Description: "Start Pods when I sign in"
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Pods"; Filename: "{app}\Pods.exe"
Name: "{userdesktop}\Pods"; Filename: "{app}\Pods.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Pods"; ValueData: """{app}\Pods.exe"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\Pods.exe"; Description: "Launch Pods"; Flags: nowait postinstall skipifsilent
; Pods' updater installs silently with /LAUNCH=1 and has exited by then, so start it again.
Filename: "{app}\Pods.exe"; Flags: nowait; Check: LaunchRequested

[Code]
function LaunchRequested: Boolean;
begin
  Result := ExpandConstant('{param:LAUNCH|0}') = '1';
end;

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM Pods.exe"; Flags: runhidden; RunOnceId: "KillPods"
