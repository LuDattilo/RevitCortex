using System;
using System.IO;
using Microsoft.Win32;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Registers the revitcortex:// URL protocol handler under HKCU (per-user,
/// no admin required). When Power BI fires a drillthrough URL like
/// <c>revitcortex://select?ids=12345,67890</c>, Windows invokes a small
/// PowerShell helper that writes a select_from_powerbi JSON-RPC request to
/// the running plugin's TCP socket. The plugin then activates Revit and
/// selects the elements via ExternalEvent.
/// </summary>
public static class ProtocolHandlerRegistrar
{
    private const string ProtocolName = "revitcortex";

    /// <summary>Folder where the helper script lives. We always overwrite to keep it in sync.</summary>
    private static string HelperFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "protocol");

    private static string HelperScriptPath => Path.Combine(HelperFolder, "revitcortex-protocol.ps1");

    /// <summary>
    /// Registers the protocol handler in HKCU. Idempotent: safe to call
    /// repeatedly; will overwrite an existing registration with the latest
    /// helper script path. Returns true if registered, false if skipped
    /// (e.g. Windows registry not available).
    /// </summary>
    public static bool Register()
    {
        WriteHelperScript();

        var port = RevitCortexApp.Instance?.Port ?? 8080;

        // HKCU\Software\Classes\revitcortex
        using var protoKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolName}");
        if (protoKey == null) return false;
        protoKey.SetValue("", "URL:RevitCortex Protocol");
        protoKey.SetValue("URL Protocol", "");

        using var iconKey = protoKey.CreateSubKey("DefaultIcon");
        iconKey?.SetValue("", "revit.exe,0");

        using var cmdKey = protoKey.CreateSubKey(@"shell\open\command");
        if (cmdKey == null) return false;

        // %1 is the full URL (e.g. revitcortex://select?ids=123,456)
        var psh = "powershell.exe";
        var cmd = $"\"{psh}\" -ExecutionPolicy Bypass -WindowStyle Hidden -NoProfile -File \"{HelperScriptPath}\" -Url \"%1\" -Port {port}";
        cmdKey.SetValue("", cmd);

        return true;
    }

    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProtocolName}", throwOnMissingSubKey: false);
        }
        catch
        {
            // Best effort
        }
    }

    private static void WriteHelperScript()
    {
        Directory.CreateDirectory(HelperFolder);
        File.WriteAllText(HelperScriptPath, HelperScriptContent);
    }

    /// <summary>
    /// The PowerShell helper:
    /// 1. parses the URL to extract action + parameters
    /// 2. writes a one-line JSON-RPC request to the plugin's TCP socket
    /// 3. exits silently
    /// Errors are written to a log file under the protocol folder for debugging.
    /// </summary>
    private const string HelperScriptContent = @"param(
    [Parameter(Mandatory=$true)][string]$Url,
    [int]$Port = 8080
)

$ErrorActionPreference = 'Continue'
$logPath = Join-Path $PSScriptRoot 'protocol.log'

function Write-Log([string]$msg) {
    Add-Content -Path $logPath -Value (""[{0:O}] {1}"" -f (Get-Date), $msg)
}

try {
    Write-Log ""URL: $Url""

    # Strip 'revitcortex://' prefix
    $stripped = $Url -replace '^revitcortex://', ''
    # Format: action?param1=value1&param2=value2
    $action, $query = $stripped -split '\?', 2

    # H6: decode with [uri]::UnescapeDataString (built into .NET, always loaded) instead
    # of [System.Web.HttpUtility]::UrlDecode, which previously failed because System.Web
    # was loaded via Add-Type only AFTER this loop ran.
    $params = @{}
    if ($query) {
        foreach ($pair in ($query -split '&')) {
            $k, $v = $pair -split '=', 2
            if ($k) { $params[$k] = [uri]::UnescapeDataString($v) }
        }
    }

    $methodMap = @{
        'select' = 'select_from_powerbi'
        'highlight' = 'select_from_powerbi'
        'isolate' = 'select_from_powerbi'
    }

    if (-not $methodMap.ContainsKey($action)) {
        Write-Log ""Unknown action: $action""
        exit 1
    }

    $methodName = $methodMap[$action]
    $idsRaw = $params['ids']
    if (-not $idsRaw) {
        Write-Log 'No ids parameter'
        exit 1
    }

    $ids = $idsRaw -split ',' | Where-Object { $_ } | ForEach-Object { [long]$_ }

    $payload = @{
        jsonrpc = '2.0'
        id = [guid]::NewGuid().ToString('N')
        method = $methodName
        params = @{
            elementIds = $ids
            action = $action
        }
    } | ConvertTo-Json -Depth 5 -Compress

    $client = New-Object System.Net.Sockets.TcpClient
    $client.Connect('127.0.0.1', $Port)
    $stream = $client.GetStream()
    $writer = New-Object System.IO.StreamWriter($stream, [System.Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $writer.WriteLine($payload)

    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
    $response = $reader.ReadLine()
    Write-Log ""Response: $response""

    $writer.Dispose()
    $reader.Dispose()
    $stream.Dispose()
    $client.Close()
}
catch {
    Write-Log ""ERROR: $_""
    exit 1
}
";
}
