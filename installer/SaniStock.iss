; Inno Setup script for SaniStock v2
; Build the app first (self-contained, bundles the .NET runtime), then compile this script:
;   dotnet publish ..\src\SaniStock.App\SaniStock.App.csproj -c Release -r win-x64 --self-contained true -o ..\publish
;   iscc SaniStock-Setup-v2.iss
; Produces Output\SaniStock-Setup-x64.exe which installs on machines WITHOUT .NET installed.

#define AppName "SaniStock"
#define AppVersion "2.0.0"
#define AppPublisher "SaniStock"
#define AppExe "SaniStock.exe"

[Setup]
AppId={{7B3D2C1A-9E44-4F6B-8A21-SANISTOCK002}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=SaniStock-Setup-x64
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Bundles the entire self-contained publish output (app + .NET runtime).
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; The database lives in %LOCALAPPDATA%\SaniStock and is intentionally NOT removed on uninstall,
; so a reinstall keeps existing data. Users can delete that folder manually to wipe all data.
