; DocAnon.iss: Inno Setup 6 script for the Windows installer.
;
; Compiled by packaging/windows/build-installer.ps1, which publishes the
; payload first and then passes the paths in as preprocessor defines:
;
;   ISCC.exe /DAppVersion=0.1.0 /DPayloadDir=...\build\win-x64 ^
;            /DOutputDir=...\dist /DRepoRoot=... DocAnon.iss
;
; The payload is a self-contained .NET 10 publish with the NER model and the
; Tesseract data inside, so the installer has no prerequisite and the installed
; app never needs the network.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
; VersionInfoVersion must be plain numeric; build-installer.ps1 passes the
; stripped form of versions such as 0.1.0-dev.
#ifndef AppVersionNumeric
  #define AppVersionNumeric "0.0.0"
#endif
#ifndef RepoRoot
  #define RepoRoot "..\.."
#endif
#ifndef PayloadDir
  #define PayloadDir RepoRoot + "\build\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir RepoRoot + "\dist"
#endif

#define AppName       "DocAnon"
#define AppPublisher  "Daniel Wagner Oliveira de Medeiros"
#define AppURL        "https://github.com/DanWBR/DocAnon"
#define AppExeName    "DocAnon.exe"
#define CliDir        "cli"
#define CliExeName    "docanon.exe"

[Setup]
; AppId identifies the product across versions. Never change it, or upgrades
; will install side by side instead of replacing the old build.
AppId={{A510C123-FF80-4F1D-9773-F4264AF3A4B6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
VersionInfoVersion={#AppVersionNumeric}
VersionInfoTextVersion={#AppVersion}
VersionInfoDescription=Anonimização local de documentos
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir={#OutputDir}
OutputBaseFilename=DocAnon-{#AppVersion}-win-x64-setup
SetupIconFile={#RepoRoot}\packaging\windows\DocAnon.ico
; About 360 MB of runtime, Avalonia, ONNX Runtime and models; LZMA2/max keeps
; the installer well below that at the cost of a slower compile.
Compression=lzma2/max
SolidCompression=yes
; Tesseract, Leptonica, PDFium and ONNX Runtime are shipped for x64 only.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-machine by default; /CURRENTUSER or the dialog installs into the user's
; profile without admin rights.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
WizardStyle=modern
DisableProgramGroupPage=yes
ShowLanguageDialog=auto
ChangesEnvironment=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
brazilianportuguese.CliPath=Adicionar a linha de comando (docanon) ao PATH
brazilianportuguese.CliGroup=Linha de comando:
brazilianportuguese.ReadMe=Leia-me
brazilianportuguese.DataKept=Os cofres de sessão e os perfis de empresa ficam em %LOCALAPPDATA%\DocAnon e não são apagados na desinstalação. Apague essa pasta se quiser removê-los.
english.CliPath=Add the command-line tool (docanon) to PATH
english.CliGroup=Command line:
english.ReadMe=Read me
english.DataKept=Session vaults and company profiles stay in %LOCALAPPDATA%\DocAnon and are not removed on uninstall. Delete that folder if you want them gone.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "clipath"; Description: "{cm:CliPath}"; GroupDescription: "{cm:CliGroup}"; Flags: unchecked

[Files]
; The whole publish tree: the app at the root, models\ (NER and tessdata) and
; the command-line tool in cli\, which reads the models from ..\models.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:ReadMe}"; Filename: "{app}\README.md"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  MachineEnvKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  UserEnvKey = 'Environment';

function CliFolder(): string;
begin
  Result := ExpandConstant('{app}\{#CliDir}');
end;

{ PATH lives under HKLM for per-machine installs and under HKCU for per-user ones. }
procedure GetEnvLocation(var Root: Integer; var Key: string);
begin
  if IsAdminInstallMode then
  begin
    Root := HKEY_LOCAL_MACHINE;
    Key := MachineEnvKey;
  end
  else
  begin
    Root := HKEY_CURRENT_USER;
    Key := UserEnvKey;
  end;
end;

procedure AddCliToPath();
var
  Root: Integer;
  Key, Path: string;
begin
  GetEnvLocation(Root, Key);
  if not RegQueryStringValue(Root, Key, 'Path', Path) then Path := '';
  if Pos(';' + Uppercase(CliFolder()) + ';', ';' + Uppercase(Path) + ';') > 0 then exit;
  if (Path <> '') and (Copy(Path, Length(Path), 1) <> ';') then Path := Path + ';';
  RegWriteExpandStringValue(Root, Key, 'Path', Path + CliFolder());
end;

procedure RemoveCliFromPath();
var
  Root: Integer;
  Key, Path, Folder: string;
  P: Integer;
begin
  GetEnvLocation(Root, Key);
  if not RegQueryStringValue(Root, Key, 'Path', Path) then exit;
  Folder := CliFolder();
  P := Pos(';' + Uppercase(Folder) + ';', ';' + Uppercase(Path) + ';');
  if P = 0 then exit;
  Delete(Path, P, Length(Folder) + 1);
  if (Length(Path) > 0) and (Copy(Path, Length(Path), 1) = ';') then Delete(Path, Length(Path), 1);
  RegWriteExpandStringValue(Root, Key, 'Path', Path);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('clipath') then AddCliToPath();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then RemoveCliFromPath();
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox(CustomMessage('DataKept'), mbInformation, MB_OK);
end;
