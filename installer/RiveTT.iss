; RiveTT — per-user installer, no administrator elevation at any point.
;
; Everything RiveTT needs lives under the user's profile, which is not a workaround:
; %APPDATA%\Autodesk\Revit\Addins\<year> is the location Autodesk documents for
; per-user add-ins. Nothing is written to HKLM, Program Files, or a service.
;
;   plugin    {userappdata}\Autodesk\Revit\Addins\<year>\RiveTT
;   manifest  {userappdata}\Autodesk\Revit\Addins\<year>\RiveTT.addin
;   server    {localappdata}\RiveTT\server\RiveTT.Server.exe
;
; The server is self-contained on purpose: framework-dependent it would need the .NET 10
; runtime under Program Files, and installing THAT is the one thing that would have
; demanded admin.
;
; Build:  .\build.ps1            (compiles both Revit targets, then calls ISCC)
;         ISCC.exe /DAppVersion=0.3.0 installer\RiveTT.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "RiveTT"
#define AppPublisher "RiveTT"

[Setup]
; A stable AppId is what lets an upgrade replace the previous install instead of
; stacking a second entry in "Installed apps". Never change it.
AppId={{7F3C9A24-5D18-4B6E-9C31-2E8A4F5B7D06}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

; The whole point. "lowest" means the installer runs as the invoking user and Windows
; never shows a UAC prompt. Any attempt to write outside the user profile would simply
; fail rather than silently escalate.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=

; {app} holds the server and the uninstaller. The plugin does not go here — Revit
; dictates its own folder, per version, and those are explicit in [Files].
DefaultDirName={localappdata}\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}
UsePreviousAppDir=yes

; x64 only: the Revit API and the server are both win-x64.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputBaseFilename=RiveTT-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
; Both Revit targets ship the same ~21 MB of third-party dependencies. Solid
; compression is what keeps the package from being twice the size for no reason.

; Restart Manager OFF, and this is load-bearing rather than a tidy-up.
;
; Enabled (the default) Inno detects Revit holding the plugin DLLs and tries to CLOSE
; it before copying. Revit refuses, and setup then asks the user to close it — or, run
; silently, aborts outright: "Some applications could not be shut down. User canceled
; the installation process." Measured with Revit open, exit code 5, nothing installed.
;
; That defeats the entire point of parking locked files as .old-<stamp> in
; CurStepChanged, which runs AFTER Restart Manager has already given up. With it off,
; the rename happens first and the copy lands on a free name, so an update proceeds
; with Revit open — which is the normal case for an agency updating mid-session.
CloseApplications=no
RestartApplications=no

WizardStyle=modern
ShowLanguageDialog=no
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\server\RiveTT.Server.exe
LicenseFile=..\LICENSE

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Files]
; The server: version-independent, installed once whatever Revit is present.
Source: "..\dist\server\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs

; One plugin payload per Revit target, installed only where that Revit really is.
; skipifsourcedoesntexist keeps a single-target build (build.ps1 -RevitVersion 2027)
; compilable instead of failing on the missing folder.
Source: "..\dist\2026\plugin\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026\RiveTT"; \
    Flags: ignoreversion recursesubdirs skipifsourcedoesntexist; Check: WantsRevit('2026')
Source: "..\dist\RiveTT.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026"; \
    Flags: ignoreversion skipifsourcedoesntexist; Check: WantsRevit('2026')

Source: "..\dist\2027\plugin\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2027\RiveTT"; \
    Flags: ignoreversion recursesubdirs skipifsourcedoesntexist; Check: WantsRevit('2027')
Source: "..\dist\RiveTT.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2027"; \
    Flags: ignoreversion skipifsourcedoesntexist; Check: WantsRevit('2027')

[UninstallDelete]
; Copies parked by the locked-file rename below, and the local runtime state.
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2026\RiveTT"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2027\RiveTT"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\RiveTT.addin.old-*"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2027\RiveTT.addin.old-*"

[Code]
const
  REVIT_ROOT = 'SOFTWARE\Autodesk\Revit\';

var
  Detected2026, Detected2027: Boolean;
  Found2026Version, Found2027Version: String;
  Stale2026: Boolean;          { Revit 2026 present but older than 2026.5 }
  ForcedYears: String;         { /REVIT=2026,2027 — for unattended IT deployment }

{ ---------------------------------------------------------------------------
  Locating Revit.

  The install path lives under HKLM\SOFTWARE\Autodesk\Revit\<year>\REVIT-nn:llll,
  where llll is a LANGUAGE code — 040C on a French install, 0409 on English. The
  subkey name must be enumerated, never hardcoded. HKLM is read-only here and
  readable by any user, so this needs no elevation.
  --------------------------------------------------------------------------- }
function GetRevitExe(Year: String): String;
var
  Names: TArrayOfString;
  I: Integer;
  Location: String;
begin
  Result := '';
  if not RegGetSubkeyNames(HKLM64, REVIT_ROOT + Year, Names) then
    Exit;

  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    if Pos('REVIT-', Names[I]) <> 1 then
      Continue;
    if not RegQueryStringValue(HKLM64, REVIT_ROOT + Year + '\' + Names[I],
                               'InstallationLocation', Location) then
      Continue;
    if Location = '' then
      Continue;
    Location := AddBackslash(Location) + 'Revit.exe';
    if FileExists(Location) then
    begin
      Result := Location;
      Exit;
    end;
  end;
end;

{ The registry "Version" value records the ORIGINAL install and is not rewritten by
  updates: a machine actually running 2026.5.0.55 still reports 2026 (26.0.4.409)
  there. Revit.exe's own file version is the only trustworthy source, and the
  2026.5 boundary matters — 2026.0 to 2026.4 run on .NET 8 and cannot load this
  plugin at all. }
function RevitIsSupported(Year: String; var VersionText: String; var TooOld: Boolean): Boolean;
var
  Exe: String;
  Major, Minor, Rev, Build: Word;
begin
  Result := False;
  TooOld := False;
  VersionText := '';

  Exe := GetRevitExe(Year);
  if Exe = '' then
    Exit;

  { GetVersionComponents takes Word, not Cardinal. }
  if not GetVersionComponents(Exe, Major, Minor, Rev, Build) then
    Exit;

  VersionText := IntToStr(Major) + '.' + IntToStr(Minor) + '.'
               + IntToStr(Rev) + '.' + IntToStr(Build);

  if Major = 26 then
  begin
    Result := Minor >= 5;
    TooOld := not Result;
  end
  else if Major >= 27 then
    Result := True;
end;

{ /REVIT=2026,2027 forces the targets regardless of what is installed. Meant for
  unattended deployment onto an image where Revit is not present yet. }
function YearIsForced(Year: String): Boolean;
begin
  Result := (ForcedYears <> '') and (Pos(Year, ForcedYears) > 0);
end;

function WantsRevit(Year: String): Boolean;
begin
  if YearIsForced(Year) then
  begin
    Result := True;
    Exit;
  end;
  if Year = '2026' then
    Result := Detected2026
  else if Year = '2027' then
    Result := Detected2027
  else
    Result := False;
end;

function InitializeSetup(): Boolean;
var
  Message: String;
  Ignored: Boolean;   { 2027 has no minimum update; its TooOld flag is meaningless }
begin
  ForcedYears := ExpandConstant('{param:REVIT|}');

  Detected2026 := RevitIsSupported('2026', Found2026Version, Stale2026);
  Detected2027 := RevitIsSupported('2027', Found2027Version, Ignored);

  Result := True;
  if Detected2026 or Detected2027 or (ForcedYears <> '') then
    Exit;

  { Nothing installable. Say which case it is — "no Revit found" and "your Revit is
    too old" call for completely different actions. }
  if Stale2026 then
    Message := 'Revit 2026 est installé en version ' + Found2026Version + ', mais RiveTT'
             + ' exige 2026.5 ou supérieur.' + #13#10#13#10
             + 'Les versions 2026.0 à 2026.4 fonctionnent sur .NET 8 et ne peuvent pas'
             + ' charger ce plugin. Appliquez la mise à jour 2026.5 depuis Autodesk'
             + ' Access, puis relancez cet installateur.'
  else
    Message := 'Aucune version compatible de Revit n''a été trouvée sur ce poste.'
             + #13#10#13#10
             + 'RiveTT prend en charge Revit 2026.5 ou supérieur, et Revit 2027.'
             + #13#10#13#10
             + 'Pour préparer un poste où Revit n''est pas encore installé, relancez'
             + ' avec :   RiveTT-Setup.exe /REVIT=2026,2027';

  MsgBox(Message, mbCriticalError, MB_OK);
  Result := False;
end;

{ ---------------------------------------------------------------------------
  Replacing files Revit currently holds open.

  Windows refuses to OVERWRITE a loaded DLL but allows RENAMING it: the open handle
  follows the old name while the new file takes its place for the next start. Parking
  the previous payload out of the way before Inno copies is what removes the "close
  Revit to update" step, which was the main friction of every upgrade.

  Inno's own restartreplace flag is not an option here — it schedules the swap through
  MoveFileEx at reboot, which needs administrator rights.
  --------------------------------------------------------------------------- }
procedure ParkLockedFiles(Folder, Stamp: String);
var
  Search: TFindRec;
  Full: String;
begin
  if not DirExists(Folder) then
    Exit;

  if FindFirst(AddBackslash(Folder) + '*', Search) then
  try
    repeat
      if Search.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then
        Continue;
      Full := AddBackslash(Folder) + Search.Name;

      { Sweep the copies parked by a previous update: nothing holds them any more. }
      if Pos('.old-', Search.Name) > 0 then
      begin
        DeleteFile(Full);
        Continue;
      end;

      { Deleting works when the file is free and fails when Revit holds it; only
        then is the rename needed. }
      if not DeleteFile(Full) then
        RenameFile(Full, Full + '.old-' + Stamp);
    until not FindNext(Search);
  finally
    FindClose(Search);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Stamp, AddinRoot: String;
  Years: TArrayOfString;
  I: Integer;
begin
  if CurStep <> ssInstall then
    Exit;

  { GetDateTimeString takes CHAR separators, not strings: passing '' raised a
    "Type Mismatch" runtime error that aborted the whole install at ssInstall.
    A null char selects the default separator, and the format string carries its
    own literal dash anyway, with no / or : placeholder for them to apply to.
    (Keep that null-char literal off the start of a line: the Inno preprocessor
    runs before Pascal comments are parsed and reads a leading hash as one of its
    own directives.) }
  Stamp := GetDateTimeString('yyyymmdd-hhnnss', #0, #0);

  { Built element by element rather than as a literal: array literals only compile on
    Inno 6.3 and later, and this must build on whatever the release machine has. }
  SetArrayLength(Years, 2);
  Years[0] := '2026';
  Years[1] := '2027';

  for I := 0 to GetArrayLength(Years) - 1 do
  begin
    if not WantsRevit(Years[I]) then
      Continue;
    AddinRoot := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\') + Years[I];
    ParkLockedFiles(AddinRoot + '\RiveTT', Stamp);
  end;

  ParkLockedFiles(ExpandConstant('{app}\server'), Stamp);
end;

{ Closing report: which Revit versions were served, and the one manual step left. }
procedure CurPageChanged(CurPageID: Integer);
var
  Summary: String;
begin
  if CurPageID <> wpFinished then
    Exit;

  Summary := '';
  if WantsRevit('2026') then
    Summary := Summary + '  • Revit 2026 (' + Found2026Version + ')' + #13#10;
  if WantsRevit('2027') then
    Summary := Summary + '  • Revit 2027 (' + Found2027Version + ')' + #13#10;

  WizardForm.FinishedLabel.Caption :=
      'RiveTT ' + '{#AppVersion}' + ' est installé pour :' + #13#10 + Summary + #13#10
    + 'Redémarrez Revit : la connexion par pipe local démarre automatiquement, sans'
    + ' port TCP. Chaque session s''ouvre en LECTURE SEULE — pressez Écriture dans le'
    + ' panneau RiveTT (onglet Compléments) pour autoriser les modifications.' + #13#10#13#10
    + 'Déclarez ensuite le serveur MCP dans votre client, avec le chemin :' + #13#10
    + ExpandConstant('{app}\server\RiveTT.Server.exe');
end;

{ Asking the OS for the process list rather than guessing a window class name: Revit's
  main window class is undocumented and has changed between releases, so a wrong guess
  would silently never fire. `find` exits 0 when it matches, 1 when it does not. }
function ProcessIsRunning(ExeName: String): Boolean;
var
  Code: Integer;
begin
  Result := False;
  if Exec(ExpandConstant('{cmd}'),
          '/C tasklist /FI "IMAGENAME eq ' + ExeName + '" /NH | find /I "' + ExeName + '"',
          '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := (Code = 0);
end;

{ Revit holds the plugin DLLs open; removing them while it runs would leave a partial
  uninstall. Unlike an update, there is no new file to park the old one out of the way
  for, so the rename trick does not help here — Revit really must be closed. }
function InitializeUninstall(): Boolean;
begin
  Result := True;

  if ProcessIsRunning('Revit.exe') then
  begin
    MsgBox('Fermez Revit avant de désinstaller RiveTT : ses DLL sont chargées en mémoire'
         + ' et ne peuvent pas être supprimées.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if ProcessIsRunning('RiveTT.Server.exe') then
  begin
    MsgBox('Le serveur MCP RiveTT tourne encore. Fermez votre client MCP avant de'
         + ' désinstaller.', mbError, MB_OK);
    Result := False;
  end;
end;
