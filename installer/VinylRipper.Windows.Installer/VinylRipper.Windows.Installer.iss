; -----------------------------------------------------------------------------
; VinylRipper.Windows.Installer — instalador de Vinyl Ripper para Windows (Inno Setup 6)
;
; No compilar este archivo a mano: build-installer.ps1 publica la app con dotnet,
; rellena las variables de abajo y llama a ISCC.exe.
;
;   /DAppVersion=1.0.0            versión (se lee del csproj)
;   /DPublishDir=...\publish      salida de dotnet publish (win-x64, self-contained)
;   /DOutputDir=...\output        dónde dejar el Setup.exe
; -----------------------------------------------------------------------------

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #error Falta /DPublishDir (usa build-installer.ps1)
#endif
#ifndef OutputDir
  #define OutputDir "output"
#endif

#define AppName      "Vinyl Ripper"
#define AppPublisher "xilonoide"
#define AppUrl       "https://github.com/xilonoide/vinyl-ripper"
#define AppExeName   "VinylRipper.Windows.exe"
#define RepoRoot     "..\.."

[Setup]
; GUID fijo: identifica la app entre versiones para que las actualizaciones se instalen encima.
AppId={{7E1B4A6C-9D2F-4C58-B7A3-5F0E2D9C1A44}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Instalador de {#AppName} para Windows

; Instalación por usuario, sin UAC: va a %LocalAppData%\Programs\Vinyl Ripper
; (igual que Inno Setup en esta máquina). Los datos de la app viven en Documentos\vinyl-ripper.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
AllowNoIcons=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

OutputDir={#OutputDir}
OutputBaseFilename=VinylRipper-Setup-{#AppVersion}-win-x64
SetupIconFile={#RepoRoot}\src\VinylRipper.Windows\Assets\vinyl.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
LicenseFile={#RepoRoot}\LICENSE

Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
WizardSizePercent=110
ShowLanguageDialog=auto
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Publicación completa (self-contained): no requiere tener .NET instalado.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#RepoRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Comment: "Convierte tu colección de Discogs en MP3"
Name: "{group}\Carpeta de descargas"; Filename: "{userdocs}\vinyl-ripper"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Restos que la app genera junto al exe (ninguno hoy, pero por si acaso). La configuración
; y las descargas en Documentos\vinyl-ripper se tratan aparte: el desinstalador pregunta.
Type: filesandordirs; Name: "{app}\logs"

[Messages]
spanish.WelcomeLabel2=Este programa instalará [name/ver] en tu equipo, sólo para tu usuario (no hace falta ser administrador).%n%nVinyl Ripper necesita ffmpeg para convertir a MP3. Si no lo tienes, instálalo con:%n    winget install Gyan.FFmpeg
english.WelcomeLabel2=This will install [name/ver] on your computer, for your user only (no administrator rights needed).%n%nVinyl Ripper needs ffmpeg to convert to MP3. If you don't have it, install it with:%n    winget install Gyan.FFmpeg

[Code]
// Aviso (no bloqueante) al final si ffmpeg no está en el PATH del usuario.
function FfmpegOnPath(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C where ffmpeg >nul 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not WizardSilent) and (not FfmpegOnPath) then
    MsgBox(ExpandConstant('{cm:FfmpegMissing}'), mbInformation, MB_OK);
end;

// Al desinstalar, ofrecer borrar Documentos\vinyl-ripper (configuración con el token, yt-dlp
// descargado y todos los MP3). Por defecto "No": ahí puede haber mucha música.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  if UninstallSilent then Exit;

  DataDir := ExpandConstant('{userdocs}\vinyl-ripper');
  if not DirExists(DataDir) then Exit;

  if MsgBox(FmtMessage(ExpandConstant('{cm:RemoveDataQuestion}'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    if not DelTree(DataDir, True, True, True) then
      MsgBox(FmtMessage(ExpandConstant('{cm:RemoveDataFailed}'), [DataDir]), mbError, MB_OK);
  end;
end;

[CustomMessages]
spanish.RemoveDataQuestion=¿Quieres eliminar también la carpeta de datos de Vinyl Ripper?%n%n%1%n%nContiene la configuración (incluido el token de Discogs), yt-dlp y TODOS los MP3 descargados. Esta acción no se puede deshacer.
english.RemoveDataQuestion=Do you also want to remove the Vinyl Ripper data folder?%n%n%1%n%nIt contains the settings (including your Discogs token), yt-dlp and ALL downloaded MP3 files. This cannot be undone.
spanish.RemoveDataFailed=No se pudo eliminar por completo la carpeta:%n%n%1%n%nPuede que algún archivo esté en uso. Bórrala a mano cuando quieras.
english.RemoveDataFailed=The folder could not be completely removed:%n%n%1%n%nSome file may be in use. You can delete it manually later.
spanish.FfmpegMissing=No se ha encontrado ffmpeg en el PATH.%n%nVinyl Ripper lo necesita para generar los MP3. Puedes instalarlo con:%n%n    winget install Gyan.FFmpeg%n%no indicar su ruta en la configuración (⚙) de la aplicación.
english.FfmpegMissing=ffmpeg was not found on your PATH.%n%nVinyl Ripper needs it to produce MP3 files. You can install it with:%n%n    winget install Gyan.FFmpeg%n%nor set its path in the application settings (⚙).
