#define MyAppName "AlfaCore"
#define MyAppPublisher "Alfa Gestion"
#define MyAppExeName "AlfaCore.exe"
#define MyAppLauncher "iniciar_AlfaCore.bat"
#define MyAppShellLauncher "abrir_dashboard_shell.bat"
#define MyReadmeName "README_INSTALACION.md"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\AlfaCoreInstaller\Input"
#endif

#ifndef OutputDir
  #define OutputDir "..\publish\AlfaCoreInstaller\Output"
#endif

[Setup]
AppId={{A3C27933-8FEA-4D63-90D0-1F334CEFC4B1}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Alfa Gestion\AlfaCore
DefaultGroupName=Alfa Gestion\AlfaCore
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir={#OutputDir}
OutputBaseFilename=AlfaCoreSetup_{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked
Name: "installservice"; Description: "Instalar como servicio de Windows (inicio automatico con el servidor)"; GroupDescription: "Servicio de Windows:"; Flags: checkedonce
Name: "openreadme"; Description: "Abrir manual de instalacion al finalizar"; GroupDescription: "Acciones sugeridas:"; Flags: checkedonce
Name: "openfirewall"; Description: "Abrir el puerto en Firewall de Windows al finalizar"; GroupDescription: "Acciones sugeridas:"; Flags: unchecked
Name: "launchapp"; Description: "Iniciar AlfaCore al finalizar"; GroupDescription: "Acciones sugeridas:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.Development.json,appsettings.Production.json,*.pdb,*.log,prereqs\*,App_Data\*"
Source: "{#SourceDir}\App_Data\updates\*"; DestDir: "{app}\App_Data\updates"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
#ifdef IncludeHostingBundle
Source: "{#SourceDir}\prereqs\dotnet-hosting-win.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsHostingBundle
#endif

[Icons]
Name: "{autoprograms}\Alfa Gestion\AlfaCore\Iniciar AlfaCore"; Filename: "{app}\{#MyAppLauncher}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\Alfa Gestion\AlfaCore\Abrir AlfaCore Shell"; Filename: "{app}\{#MyAppShellLauncher}"; WorkingDir: "{app}"; IconFilename: "{app}\AlfaCoreShell.exe"
Name: "{autoprograms}\Alfa Gestion\AlfaCore\Manual de instalacion"; Filename: "{app}\{#MyReadmeName}"
Name: "{autodesktop}\AlfaCore"; Filename: "{app}\{#MyAppLauncher}"; WorkingDir: "{app}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{cmd}"; Parameters: "/c ""{app}\instalar_servicio.bat"" /silent"; Flags: runhidden waituntilterminated; Tasks: installservice
Filename: "{app}\{#MyReadmeName}"; Description: "Abrir manual de instalacion"; Flags: postinstall shellexec skipifsilent; Tasks: openreadme
Filename: "{cmd}"; Parameters: "/c ""{app}\abrir_firewall.bat"""; Description: "Abrir el puerto en Firewall de Windows"; Flags: postinstall runhidden waituntilterminated skipifsilent; Tasks: openfirewall
Filename: "{cmd}"; Parameters: "/c ""{app}\{#MyAppLauncher}"""; Description: "Iniciar AlfaCore"; Flags: postinstall skipifsilent; Tasks: launchapp

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c ""{app}\desinstalar_servicio.bat"" /silent"; Flags: runhidden waituntilterminated

[UninstallDelete]
Type: files; Name: "{app}\shell_backend_stdout.log"
Type: files; Name: "{app}\shell_backend_stderr.log"
Type: files; Name: "{app}\backend_startup.log"

[Code]
function IsNetRuntimeInstalled(RuntimeId: string): Boolean;
var
  SubkeyNames: TArrayOfString;
  KeyPath: string;
  I: Integer;
begin
  Result := False;
  KeyPath := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\' + RuntimeId;
  if RegGetSubkeyNames(HKLM64, KeyPath, SubkeyNames) then
    for I := 0 to GetArrayLength(SubkeyNames) - 1 do
      if Copy(SubkeyNames[I], 1, 2) = '8.' then
      begin
        Result := True;
        Exit;
      end;
end;

function NeedsHostingBundle(): Boolean;
begin
  Result := not IsNetRuntimeInstalled('Microsoft.AspNetCore.App');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  BundlePath: string;
begin
  Result := '';
  if not NeedsHostingBundle() then
    Exit;

  BundlePath := ExpandConstant('{tmp}\dotnet-hosting-win.exe');

  if not FileExists(BundlePath) then
  begin
    MsgBox(
      'No se encontro el instalador de .NET 8 Hosting Bundle.' + #13#10#13#10 +
      'Instalelo manualmente antes de usar la aplicacion:' + #13#10 +
      'https://dotnet.microsoft.com/es-es/download/dotnet/8.0' + #13#10 +
      '(descargar "Hosting Bundle" para Windows)',
      mbInformation, MB_OK);
    Exit;
  end;

  MsgBox(
    'Se instalara el .NET 8 Hosting Bundle.' + #13#10 +
    'Este proceso puede tardar unos minutos. Haga clic en Aceptar para continuar.',
    mbInformation, MB_OK);

  if ShellExec('runas', BundlePath, '/passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode = 3010 then
      NeedsRestart := True
    else if ResultCode <> 0 then
      MsgBox(
        'La instalacion de .NET 8 Hosting Bundle termino con codigo: ' + IntToStr(ResultCode) + #13#10 +
        'Si la aplicacion no inicia, instalelo manualmente desde:' + #13#10 +
        'https://dotnet.microsoft.com/es-es/download/dotnet/8.0',
        mbInformation, MB_OK);
  end
  else
    MsgBox(
      'No se pudo ejecutar el instalador de .NET 8 Hosting Bundle.' + #13#10#13#10 +
      'Instalelo manualmente antes de usar la aplicacion:' + #13#10 +
      'https://dotnet.microsoft.com/es-es/download/dotnet/8.0' + #13#10 +
      '(descargar "Hosting Bundle" para Windows)',
      mbInformation, MB_OK);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  #ifndef IncludeHostingBundle
  if NeedsHostingBundle() then
  begin
    if MsgBox(
      'No se detecto ASP.NET Core 8 Runtime en este equipo.' + #13#10#13#10 +
      'Si ya lo tiene instalado (SDK o Hosting Bundle), puede continuar igual.' + #13#10 +
      'Si no lo tiene, la aplicacion no iniciara correctamente.' + #13#10#13#10 +
      '¿Desea abrir la pagina de descarga del Hosting Bundle?',
      mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/es-es/download/dotnet/8.0', '', '', SW_SHOW, ewNoWait, ErrorCode);
  end;
  #endif
end;
