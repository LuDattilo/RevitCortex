param(
    [string]$Method = "say_hello",
    [string]$Params = "{}",
    [int]$Port = 8080
)

$msg = '{"jsonrpc":"2.0","id":1,"method":"' + $Method + '","params":' + $Params + '}'

try {
    $client = New-Object System.Net.Sockets.TcpClient
    $client.Connect("127.0.0.1", $Port)
    $stream = $client.GetStream()
    $writer = New-Object System.IO.StreamWriter($stream)
    $reader = New-Object System.IO.StreamReader($stream)

    $writer.WriteLine($msg)
    $writer.Flush()

    $client.ReceiveTimeout = 10000
    $response = $reader.ReadLine()
    Write-Output $response

    $client.Close()
}
catch {
    Write-Output "ERROR: $_"
}
