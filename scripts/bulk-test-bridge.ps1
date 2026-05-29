# Bulk-test helper: calls the RevitCortex plugin bridge directly over JSON-RPC on port 8080.
# Bypasses the (stale) MCP server in this session so we exercise the freshly-deployed plugin.
# Dot-source this file, then use Invoke-RC.

function Invoke-RC {
    param(
        [Parameter(Mandatory)] [string]$Method,
        [hashtable]$Params = @{},
        [int]$TimeoutSec = 120,
        [int]$Port = 8080
    )
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        $client.ReceiveTimeout = $TimeoutSec * 1000
        $client.SendTimeout = $TimeoutSec * 1000
        $stream = $client.GetStream()
        $req = @{ jsonrpc = '2.0'; method = $Method; params = $Params; id = 'bulk2' } | ConvertTo-Json -Depth 30 -Compress
        $bytes = [Text.Encoding]::UTF8.GetBytes($req + "`n")
        $stream.Write($bytes, 0, $bytes.Length); $stream.Flush()
        $sr = New-Object IO.StreamReader($stream, [Text.Encoding]::UTF8)
        return $sr.ReadLine()
    }
    catch {
        return (@{ error = @{ transport = $_.Exception.Message } } | ConvertTo-Json -Compress)
    }
    finally { $client.Close() }
}

# Compact one-line printer: status + first chars of result/error.
function Show-RC {
    param([string]$Label, [string]$Json, [int]$Len = 280)
    $tag = if ($Json -match '"error"') { 'ERR ' } elseif ($Json -match '"success"\s*:\s*false') { 'FAIL' } else { 'OK  ' }
    $snip = $Json.Substring(0, [Math]::Min($Len, $Json.Length))
    Write-Host ("[{0}] {1}: {2}" -f $tag, $Label, $snip)
}
