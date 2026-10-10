#define MyAppName "NemBoard"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "NemBoard contributors"
#define MyAppExeName "NemBoard.exe"

[Setup]
AppId={{2AE3EC44-65E9-4F8F-8D26-D0368A42B3C4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=NemBoard-Setup-x64
SetupIconFile=..\Source\Soundboard.App\Assets\NemBoard.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=zip
SolidCompression=no
WizardStyle=modern
MinVersion=10.0.10240
ShowLanguageDialog=yes
LanguageDetectionMethod=none
UsePreviousLanguage=no
UsePreviousTasks=no
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=NemBoard for Windows 10 and 11 x64
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "audio-setup-en.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; InfoBeforeFile: "audio-setup.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; InfoBeforeFile: "audio-setup-es.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; InfoBeforeFile: "audio-setup-de.txt"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"; InfoBeforeFile: "audio-setup-it.txt"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"; InfoBeforeFile: "audio-setup-pt.txt"

[CustomMessages]
english.InstallVBCable=Install VB-CABLE automatically if missing
english.AudioDriver=Audio driver:
english.DesktopShortcut=Create a desktop shortcut
english.ExtraShortcuts=Additional shortcuts:
french.InstallVBCable=Installer automatiquement VB-CABLE s’il est absent
french.AudioDriver=Pilote audio :
french.DesktopShortcut=Créer un raccourci sur le Bureau
french.ExtraShortcuts=Raccourcis supplémentaires :
spanish.InstallVBCable=Instalar VB-CABLE automáticamente si falta
spanish.AudioDriver=Controlador de audio:
spanish.DesktopShortcut=Crear un acceso directo en el escritorio
spanish.ExtraShortcuts=Accesos directos adicionales:
german.InstallVBCable=VB-CABLE automatisch installieren, falls es fehlt
german.AudioDriver=Audiotreiber:
german.DesktopShortcut=Desktopverknüpfung erstellen
german.ExtraShortcuts=Zusätzliche Verknüpfungen:
italian.InstallVBCable=Installa automaticamente VB-CABLE se assente
italian.AudioDriver=Driver audio:
italian.DesktopShortcut=Crea un collegamento sul desktop
italian.ExtraShortcuts=Collegamenti aggiuntivi:
portuguese.InstallVBCable=Instalar automaticamente o VB-CABLE se estiver em falta
portuguese.AudioDriver=Controlador de áudio:
portuguese.DesktopShortcut=Criar atalho no ambiente de trabalho
portuguese.ExtraShortcuts=Atalhos adicionais:

[Tasks]
Name: "vbcable"; Description: "{cm:InstallVBCable}"; GroupDescription: "{cm:AudioDriver}"
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ExtraShortcuts}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "VB-CABLE-NOTICE.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "vendor\VBCABLE_Driver_Pack45\*"; DestDir: "{tmp}\Soundboard-VBCABLE"; Flags: ignoreversion recursesubdirs createallsubdirs deleteafterinstall

[InstallDelete]
Type: files; Name: "{app}\Soundboard.exe"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Désinstaller {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Check: ShouldLaunchNemBoard

[Code]
var
  VBCableInstalledBySetup: Boolean;

function IsVBCableInstalled: Boolean;
var
  ResultCode: Integer;
  PowerShellArgs: String;
begin
  PowerShellArgs := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$found = Get-PnpDevice -Class AudioEndpoint -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq ''OK'' -and $_.FriendlyName -match ''CABLE (Input|Output)'' }; if ($found) { exit 0 } else { exit 1 }"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), PowerShellArgs, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  SetupPath: String;
  LanguagePath: String;
  LanguageCode: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  LanguagePath := ExpandConstant('{localappdata}\NemBoard\setup-language.txt');
  ForceDirectories(ExtractFileDir(LanguagePath));
  if ActiveLanguage = 'french' then LanguageCode := 'fr'
  else if ActiveLanguage = 'spanish' then LanguageCode := 'es'
  else if ActiveLanguage = 'german' then LanguageCode := 'de'
  else if ActiveLanguage = 'italian' then LanguageCode := 'it'
  else if ActiveLanguage = 'portuguese' then LanguageCode := 'pt'
  else LanguageCode := 'en';
  SaveStringToFile(LanguagePath, LanguageCode, False);

  if (not WizardIsTaskSelected('vbcable')) or IsVBCableInstalled then
    Exit;

  SetupPath := ExpandConstant('{tmp}\Soundboard-VBCABLE\VBCABLE_Setup_x64.exe');
  if not FileExists(SetupPath) then
  begin
    MsgBox('VB-CABLE is missing. NemBoard will be installed, but microphone routing will remain unavailable.', mbError, MB_OK);
    Exit;
  end;

  if ShellExec('runas', SetupPath, '-i -h', ExtractFileDir(SetupPath), SW_HIDE, ewWaitUntilTerminated, ResultCode) and ((ResultCode = 0) or (ResultCode = 3010)) then
  begin
    VBCableInstalledBySetup := True;
  end
  else
  begin
    MsgBox('VB-CABLE could not be installed. You can run its installer again from the official VB-Audio website.', mbError, MB_OK);
  end;
end;

function NeedRestart: Boolean;
begin
  Result := VBCableInstalledBySetup;
end;

function ShouldLaunchNemBoard: Boolean;
begin
  Result := not VBCableInstalledBySetup;
end;
