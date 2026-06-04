; RevitCortex Inno Setup Installer
; Generates a single-file .exe installer from the release/ folder.
;
; The heavy lifting (Claude Desktop JSON merge, Revit addin deploy with ACL fallback,
; Git auto-install) lives in PowerShell helpers under dist-lib/. The Inno step just
; copies binaries and invokes the shared scripts so that .exe and .zip installs
; behave identically.
;
; Usage:
;   1. Run build-release.ps1 -Version "1.0.0" first.
;   2. Then: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\RevitCortex.iss
;   Output: installer\Output\RevitCortex-Setup.exe

#define MyAppName "RevitCortex"
#define MyAppVersion "1.0.34"
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

[InstallDelete]
; Wipe previous server folder before writing new files. Prevents hybrid deploys
; (mismatched runtimeconfig.json over stale self-contained runtime DLLs) from
; corrupting the exe launch. Safe: no user data lives under server/.
Type: filesandordirs; Name: "{userappdata}\.revitcortex\server"

[Files]
; MCP Server (self-contained C# EXE ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â no Node.js dependency)
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

; Shared PowerShell helpers (reused by both .zip and .exe install paths)
Source: "..\distribution\lib\*"; DestDir: "{userappdata}\.revitcortex\dist-lib"; Components: server; Flags: ignoreversion recursesubdirs createallsubdirs

; Install/uninstall scripts (installed for manual re-run)
Source: "..\distribution\install.ps1"; DestDir: "{userappdata}\.revitcortex"; Flags: ignoreversion
Source: "..\distribution\uninstall.ps1"; DestDir: "{userappdata}\.revitcortex"; Flags: ignoreversion

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
Type: filesandordirs; Name: "{userappdata}\.revitcortex\dist-lib"

[Run]
; Best-effort Git install (winget first, then Git-for-Windows release as fallback).
; Shared GitInstall.ps1 handles arch detection. Non-fatal on failure.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""& {{ . '{userappdata}\.revitcortex\dist-lib\GitInstall.ps1'; Ensure-Git | Out-Null }}"""; \
  StatusMsg: "Verifica/installazione Git..."; \
  Flags: runhidden waituntilterminated; \
  Components: server

; Claude Desktop configuration via the same JSON-safe helper used by install.ps1.
; This replaces the old inline one-liner that overwrote existing mcpServers entries.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""& {{ . '{userappdata}\.revitcortex\dist-lib\ClaudeConfig.ps1'; $exe = Join-Path $env:USERPROFILE '.revitcortex\server\RevitCortex.Server.exe'; $cfg = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'; try {{ Merge-ClaudeMcpServer -ConfigPath $cfg -ServerName 'revitcortex' -Command $exe | Out-Null }} catch {{ Write-Host ('Claude Desktop config update failed: ' + $_) }} }}"""; \
  StatusMsg: "Configurazione Claude Desktop..."; \
  Flags: runhidden waituntilterminated; \
  Components: server

[UninstallRun]
; Remove revitcortex entry from Claude Desktop without touching other MCP servers.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""& {{ . '{userappdata}\.revitcortex\dist-lib\ClaudeConfig.ps1'; $cfg = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'; try {{ Remove-ClaudeMcpServer -ConfigPath $cfg -ServerName 'revitcortex' | Out-Null }} catch {{}} }}"""; \
  Flags: runhidden waituntilterminated; \
  RunOnceId: "RemoveClaudeDesktop"

[Code]
function RevitDirExists(Version: String): Boolean;
begin
  Result := DirExists(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Version));
end;
