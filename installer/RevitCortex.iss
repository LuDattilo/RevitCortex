; RevitCortex Inno Setup Installer
; Generates a single-file .exe installer from the release/ folder
;
; Usage:
;   1. Run build-release.ps1 -Version "1.0.0" first
;   2. Then: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\RevitCortex.iss
;   Output: installer\Output\RevitCortex-Setup.exe

#define MyAppName "RevitCortex"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Luigi Dattilo"
#define MyAppURL "https://github.com/LuDattilo/RevitCortex"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={userappdata}\.revitcortex
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=RevitCortex-Setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayName={#MyAppName}
DisableDirPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
italian.WelcomeLabel1=Benvenuto nell'installazione di RevitCortex
italian.WelcomeLabel2=Questo programma installera' RevitCortex, assistente AI per Autodesk Revit.%n%nVersioni supportate: Revit 2023, 2024, 2025, 2026, 2027.%n%nE' consigliabile chiudere Revit prima di procedere.
italian.FinishedLabel=L'installazione di RevitCortex e' completata.%n%nRiavvia Revit per caricare il plugin.

[Types]
Name: "full"; Description: "Installazione completa (tutte le versioni Revit rilevate)"
Name: "custom"; Description: "Scegli le versioni"; Flags: iscustom

[Components]
Name: "server"; Description: "MCP Server (richiesto)"; Types: full custom; Flags: fixed
Name: "r23"; Description: "Plugin Revit 2023"; Types: full; Check: RevitDirExists('2023')
Name: "r24"; Description: "Plugin Revit 2024"; Types: full; Check: RevitDirExists('2024')
Name: "r25"; Description: "Plugin Revit 2025"; Types: full; Check: RevitDirExists('2025')
Name: "r26"; Description: "Plugin Revit 2026"; Types: full; Check: RevitDirExists('2026')
Name: "r27"; Description: "Plugin Revit 2027"; Types: full; Check: RevitDirExists('2027')

[Files]
; MCP Server
Source: "..\release\server\*"; DestDir: "{userappdata}\.revitcortex\server"; Components: server; Flags: ignoreversion recursesubdirs createallsubdirs

; Plugin DLLs per version
Source: "..\release\plugin\R23\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023\RevitCortex"; Components: r23; Flags: ignoreversion recursesubdirs
Source: "..\release\plugin\R24\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024\RevitCortex"; Components: r24; Flags: ignoreversion recursesubdirs
Source: "..\release\plugin\R25\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025\RevitCortex"; Components: r25; Flags: ignoreversion recursesubdirs
Source: "..\release\plugin\R26\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026\RevitCortex"; Components: r26; Flags: ignoreversion recursesubdirs
Source: "..\release\plugin\R27\*"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2027\RevitCortex"; Components: r27; Flags: ignoreversion recursesubdirs

; .addin manifest per version
Source: "..\release\RevitCortex.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2023"; Components: r23; Flags: ignoreversion
Source: "..\release\RevitCortex.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2024"; Components: r24; Flags: ignoreversion
Source: "..\release\RevitCortex.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2025"; Components: r25; Flags: ignoreversion
Source: "..\release\RevitCortex.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2026"; Components: r26; Flags: ignoreversion
Source: "..\release\RevitCortex.addin"; DestDir: "{commonappdata}\Autodesk\Revit\Addins\2027"; Components: r27; Flags: ignoreversion

; Install/uninstall scripts (for manual use)
Source: "..\release\install.ps1"; DestDir: "{userappdata}\.revitcortex"; Flags: ignoreversion
Source: "..\release\uninstall.ps1"; DestDir: "{userappdata}\.revitcortex"; Flags: ignoreversion

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2023\RevitCortex"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\RevitCortex"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\RevitCortex"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\RevitCortex"
Type: filesandordirs; Name: "{commonappdata}\Autodesk\Revit\Addins\2027\RevitCortex"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2023\RevitCortex.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\RevitCortex.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\RevitCortex.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2026\RevitCortex.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2027\RevitCortex.addin"
Type: filesandordirs; Name: "{userappdata}\.revitcortex\server"

[Run]
; npm install after server files are copied
Filename: "cmd.exe"; Parameters: "/c cd /d ""{userappdata}\.revitcortex\server"" && npm install --production --silent 2>nul"; StatusMsg: "Installazione dipendenze server..."; Flags: runhidden waituntilterminated; Components: server
; Configure Claude Desktop
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""& {{ $sp = Join-Path $env:USERPROFILE '.revitcortex\server\build\index.js'; $cp = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'; $cd = Split-Path $cp; if (!(Test-Path $cd)) {{ New-Item -ItemType Directory -Path $cd -Force | Out-Null }}; $entry = @{{ command='node'; args=@($sp) }}; if (Test-Path $cp) {{ try {{ $c = Get-Content $cp -Raw | ConvertFrom-Json; if (!$c.mcpServers) {{ $c | Add-Member -NotePropertyName mcpServers -NotePropertyValue @{{}} -Force }}; $c.mcpServers | Add-Member -NotePropertyName revitcortex -NotePropertyValue $entry -Force; $c | ConvertTo-Json -Depth 10 | Set-Content $cp -Encoding UTF8 }} catch {{ @{{ mcpServers = @{{ revitcortex = $entry }} }} | ConvertTo-Json -Depth 10 | Set-Content $cp -Encoding UTF8 }} }} else {{ @{{ mcpServers = @{{ revitcortex = $entry }} }} | ConvertTo-Json -Depth 10 | Set-Content $cp -Encoding UTF8 }} }}"""; StatusMsg: "Configurazione Claude Desktop..."; Flags: runhidden waituntilterminated; Components: server

[UninstallRun]
; Remove Claude Desktop config entry
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""& {{ $cp = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'; if (Test-Path $cp) {{ try {{ $c = Get-Content $cp -Raw | ConvertFrom-Json; if ($c.mcpServers -and $c.mcpServers.revitcortex) {{ $c.mcpServers.PSObject.Properties.Remove('revitcortex'); $c | ConvertTo-Json -Depth 10 | Set-Content $cp -Encoding UTF8 }} }} catch {{}} }} }}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveClaudeDesktop"

[Code]
var
  GlobalResultCode: Integer;

function RevitDirExists(Version: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version));
end;

function NodeJsInstalled(): Boolean;
var
  RC: Integer;
begin
  Result := Exec('cmd.exe', '/c node --version', '', SW_HIDE, ewWaitUntilTerminated, RC) and (RC = 0);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not NodeJsInstalled() then
  begin
    if MsgBox('Node.js non trovato. E'' necessario per il server MCP.'#13#10#13#10'Vuoi aprire la pagina di download di Node.js?'#13#10'Dopo l''installazione di Node.js, riesegui questo installer.',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://nodejs.org', '', '', SW_SHOWNORMAL, ewNoWait, GlobalResultCode);
    end;
    Result := False;
  end;
end;
