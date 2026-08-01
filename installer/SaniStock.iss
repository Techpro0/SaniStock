; Inno Setup script for SaniStock.
;
; Build the app first (self-contained — bundles the .NET runtime), then compile this script:
;   dotnet publish ..\src\SaniStock.App\SaniStock.App.csproj -c Release -r win-x64 --self-contained true -o ..\publish
;   iscc SaniStock.iss
; Produces Output\SaniStock-Setup-x64.exe, which installs on machines WITHOUT .NET installed.
;
; ---------------------------------------------------------------------------------------------
; 2.2.0 adds Brands. Ware leaves the kiln undifferentiated and only becomes a brand when someone
; packs it, so the first launch after upgrading runs an EF Core migration that REWRITES EXISTING
; ROWS rather than just adding columns:
;
;   * every stock balance splits into a shared unpacked pool plus one row per brand;
;   * all stock already packed is assigned to a seeded "Unbranded" brand;
;   * the movement ledger is split to match, so recalculating balances reproduces the same numbers;
;   * every existing order line and packing entry is assigned to "Unbranded".
;
; Two consequences drive the [Code] section below:
;
;   1. A pre-install BACKUP matters more than it did for previous releases — this is not a change a
;      user can unwind by hand. See PrepareToInstall.
;   2. DOWNGRADING is now genuinely unsafe. OrderLines.BrandId is NOT NULL with no default, so an
;      older build would fail outright the first time someone books an order, and would misread the
;      per-brand stock rows before that. See InitializeSetup, which blocks it.
; ---------------------------------------------------------------------------------------------

#define AppName "SaniStock"
#define AppVersion "2.2.0"
#define AppPublisher "SaniStock"
#define AppExe "SaniStock.exe"
#define PublishDir "..\publish"

; Fail the build rather than silently shipping a missing or stale payload. Forgetting the publish step
; is the easy mistake here: [Files] below globs whatever happens to be in ..\publish.
#if !FileExists(PublishDir + "\" + AppExe)
  #error Publish output not found. Run the dotnet publish command at the top of this file first.
#endif

; Print what is actually being packaged, so a stale publish folder is visible at compile time.
#define PackagedVersion GetVersionNumbersString(PublishDir + "\" + AppExe)
#pragma message "Packaging " + AppExe + " file version " + PackagedVersion + " as setup version " + AppVersion

[Setup]
; Upgrade identity — must never change, or new versions install alongside the old one
; instead of upgrading it. Kept exactly as first shipped, malformed GUID and all.
; NOTE: UninstallSubkey in [Code] embeds this same id. If you ever touch one, touch both.
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
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=..\src\SaniStock.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}

; .NET 8 requires Windows 10 1607 (build 14393) or later. Blocking older builds up front gives a clear
; message instead of an install that succeeds and then fails to start.
MinVersion=10.0.14393

; Shut down a running SaniStock via Restart Manager instead of failing on locked files when upgrading.
; The app is not relaunched automatically — the [Run] entry below covers that.
CloseApplications=yes
RestartApplications=no

; Shown in the setup .exe's own file properties, so two builds can be told apart on disk.
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright={#AppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Bundles the entire self-contained publish output (app + .NET runtime + QuestPDF's Lato fonts).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; The database lives in %LOCALAPPDATA%\SaniStock and is intentionally NOT removed on uninstall,
; so a reinstall keeps existing data. Users can delete that folder manually to wipe all data.

[Code]
const
  { Inno's own uninstall registry key is AppId + '_is1'. Kept in sync with [Setup] AppId by hand.
    If the two ever drift, InstalledVersion simply finds nothing and the downgrade check allows the
    install — it fails OPEN, so a mistake here can never block a legitimate upgrade. }
  UninstallSubkey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7B3D2C1A-9E44-4F6B-8A21-SANISTOCK002}_is1';

var
  BackupPaths: TArrayOfString;   { every file actually copied aside, in copy order }
  BackupCount: Integer;

{ ---- Downgrade protection ------------------------------------------------------------------- }

{ The version currently installed, or '' if SaniStock is not installed (or cannot be read). }
function InstalledVersion: String;
begin
  Result := '';
  { HKLM resolves to the 64-bit view in 64-bit install mode; check the 32-bit view too, in case an
    older setup ever ran in 32-bit mode on this machine. }
  if not RegQueryStringValue(HKLM, UninstallSubkey, 'DisplayVersion', Result) then
    if not RegQueryStringValue(HKLM32, UninstallSubkey, 'DisplayVersion', Result) then
      Result := '';
end;

{ Refuses to install over a NEWER SaniStock. Once 2.2.0 has run, the database carries per-brand
  stock rows and a NOT NULL OrderLines.BrandId; an older build would silently misread the split and
  then fail hard the first time someone books an order. EF Core will not migrate backwards, so
  there is no clean recovery except restoring a backup — much better to stop here.

  Worth being clear about the limit: this only guards setups that CONTAIN this check. It stops a
  future 2.3.0 database being clobbered by re-running 2.2.0's setup, but it cannot stop someone
  running the already-shipped 2.1.0 installer over 2.2.0 — that binary has no such check and cannot
  be changed retroactively. The pre-install backup is the safety net for that case. }
function InitializeSetup(): Boolean;
var
  Installed: String;
  InstalledVer, SetupVer: Int64;
begin
  Result := True;

  Installed := InstalledVersion;
  if Installed = '' then
    Exit;

  { Anything unparseable is treated as "no opinion" rather than as a reason to block. }
  if not StrToVersion(Installed, InstalledVer) then
    Exit;
  if not StrToVersion('{#AppVersion}', SetupVer) then
    Exit;
  if ComparePackedVersion(InstalledVer, SetupVer) <= 0 then
    Exit;

  Result := False;
  MsgBox('A newer version of ' + '{#AppName}' + ' is already installed.' + #13#10#13#10 +
         'Installed: ' + Installed + #13#10 +
         'This setup: ' + '{#AppVersion}' + #13#10#13#10 +
         'Going back to an older version is not supported: the newer version upgrades the database ' +
         'format, and older versions cannot read it correctly. Installing this one would risk ' +
         'damaging your stock and order data.' + #13#10#13#10 +
         'To genuinely go back, uninstall ' + '{#AppName}' + ', delete or move the database in' + #13#10 +
         ExpandConstant('{localappdata}') + '\' + '{#AppName}' + #13#10 +
         'and restore a backup taken before the upgrade.',
         mbCriticalError, MB_OK);
end;

{ ---- Pre-install backup --------------------------------------------------------------------- }

function DatabasePath: string;
begin
  { Note: in administrative install mode this resolves to the profile of whoever is running Setup.
    That is the same profile in the normal case (a user elevating their own account via UAC), but if a
    different administrator account installs, the file simply will not be found and the backup is
    skipped — Setup never claims to have made a backup it did not make. }
  Result := ExpandConstant('{localappdata}') + '\{#AppName}\sanistock.db';
end;

{ Copies one file aside and records it. Returns False only when the copy genuinely failed. }
function BackupOne(const Source, Stamp: string): Boolean;
var
  Target: string;
  SourceSize, TargetSize: Int64;
begin
  Target := Source + '.before-{#AppVersion}-' + Stamp + '.bak';

  Result := CopyFile(Source, Target, True);
  if not Result then
  begin
    Log('FAILED to back up ' + Source);
    Exit;
  end;

  { A copy that reports success but lands a different size is worse than no copy, because the user
    would be told they have a backup. Treat that as a failure. }
  if (not FileSize64(Source, SourceSize)) or (not FileSize64(Target, TargetSize))
     or (SourceSize <> TargetSize) then
  begin
    Log('Backup of ' + Source + ' does not match the original — discarding it.');
    DeleteFile(Target);
    Result := False;
    Exit;
  end;

  SetArrayLength(BackupPaths, BackupCount + 1);
  BackupPaths[BackupCount] := Target;
  BackupCount := BackupCount + 1;
  Log('Backed up ' + Source + ' to ' + Target);
end;

{ SQLite may leave -journal / -wal / -shm files beside the database. They normally vanish on a clean
  close, but after a crash they hold committed data the .db alone does not, so a backup that omits
  them can quietly lose the last transactions. }
procedure BackupSiblings(const DbFile, Stamp: string);
var
  Dir: string;
  FindRec: TFindRec;
begin
  Dir := ExtractFilePath(DbFile);
  if not FindFirst(DbFile + '-*', FindRec) then
    Exit;
  try
    repeat
      { Skip the sibling backups left by earlier upgrades — they match the same wildcard. }
      if Pos('.bak', Lowercase(FindRec.Name)) = 0 then
        BackupOne(Dir + FindRec.Name, Stamp);
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

{ Copies the existing database aside before any files are replaced. This release's first launch
  splits every stock row per brand and rewrites the ledger to match, which is not something a user
  can unwind by hand. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Source, Stamp: string;
begin
  Result := '';
  BackupCount := 0;
  SetArrayLength(BackupPaths, 0);

  Source := DatabasePath;
  if not FileExists(Source) then
  begin
    Log('No existing database at ' + Source + ' — nothing to back up (fresh install).');
    Exit;
  end;

  { One timestamp for the whole set, so the database and its journal files stay recognisable
    as belonging to the same backup. }
  Stamp := GetDateTimeString('yyyymmdd-hhnnss', '-', '-');

  if BackupOne(Source, Stamp) then
  begin
    BackupSiblings(Source, Stamp);
    Exit;
  end;

  if MsgBox('Setup could not copy your existing SaniStock database:' + #13#10#13#10 +
            Source + #13#10#13#10 +
            'This version rewrites the database on first start to add Brands, so a backup is ' +
            'strongly recommended before continuing.' + #13#10#13#10 +
            'Continue without a backup?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
    Result := 'Setup was stopped so you can back up your database first. ' +
              'Copy sanistock.db from ' + ExpandConstant('{localappdata}') + '\{#AppName} ' +
              'to a safe place, then run Setup again.';
end;

{ ---- Finish page ----------------------------------------------------------------------------- }

{ Tell the user where the backup went and what the first launch will do, on the page they are
  already reading. }
procedure CurPageChanged(CurPageID: Integer);
var
  Details: string;
  I: Integer;
begin
  if CurPageID <> wpFinished then
    Exit;

  Details := '';
  if BackupCount > 0 then
  begin
    Details := #13#10#13#10 + 'Your previous database was backed up to:';
    for I := 0 to BackupCount - 1 do
      Details := Details + #13#10 + BackupPaths[I];
  end;

  Details := Details + #13#10#13#10 +
    'The first start of {#AppName} will upgrade the database to support Brands. ' +
    'Everything you have already packed is assigned to a brand called "Unbranded", and nothing ' +
    'you have booked needs re-entering.' + #13#10#13#10 +
    'Next: add your real brands under Setup Lists > Brands, pack new stock under them, and ' +
    'switch "Unbranded" off once the old stock has sold through.';

  WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + Details;
end;
