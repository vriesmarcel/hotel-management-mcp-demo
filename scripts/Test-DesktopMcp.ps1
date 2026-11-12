param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not ('HotelDemoWindow' -as [type])) {
    Add-Type @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
public static class HotelDemoWindow {
    private delegate bool Callback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, Callback callback, IntPtr state);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    public static bool Close(Process process) {
        bool found = false;
        foreach (ProcessThread thread in process.Threads)
            EnumThreadWindows((uint)thread.Id, (window, state) => {
                var text = new StringBuilder(512); GetWindowText(window, text, text.Capacity);
                if (text.ToString().Contains("MCP: http://127.0.0.1:5057/mcp/ (PID " + process.Id + ")"))
                    found = PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); // WM_CLOSE, owned login only
                return true;
            }, IntPtr.Zero);
        return found;
    }
}
'@
}
if (-not (Test-Path (Join-Path $root 'Project_HMS\ConnectionStrings.local.config'))) { throw 'Run Configure-Database.ps1 first.' }
$probe = [Net.Sockets.TcpClient]::new()
try {
    try { $probe.Connect('127.0.0.1',5057) } catch [Net.Sockets.SocketException] { }
    if ($probe.Connected) { throw 'Port 5057 is occupied. Close the demo application before this test.' }
} finally { $probe.Dispose() }
$oldToken = $env:HMS_MCP_TOKEN; $oldEnabled = $env:HMS_MCP_ENABLED
$oldConnection = $env:HMS_CONNECTION_STRING
$app = $null
try {
    $env:HMS_MCP_TOKEN = $null
    $env:HMS_MCP_ENABLED = $null
    $env:HMS_CONNECTION_STRING = $null
    $headers = @{ Authorization = 'Bearer 123456'; Accept = 'application/json, text/event-stream'; 'MCP-Protocol-Version' = '2025-11-25' }
    $exe = Join-Path $root "Project_HMS\bin\$Configuration\Project_HMS.exe"
    $app = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
    $discovery = @{ jsonrpc='2.0'; id=1; method='tools/list'; params=@{} } | ConvertTo-Json -Compress
    $ready = $false
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        if ($app.HasExited) { throw 'Desktop application exited before MCP became ready.' }
        try {
            $response = Invoke-WebRequest 'http://127.0.0.1:5057/mcp/' -Method Post -Headers $headers -ContentType 'application/json' -Body $discovery -UseBasicParsing -TimeoutSec 2
            if ($response.Content -match 'GetAvailableRooms') { $ready=$true; break }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw 'Embedded MCP discovery did not become ready.' }
    $body = @{ jsonrpc='2.0'; id=2; method='tools/call'; params=@{ name='GetAvailableRooms'; arguments=@{ checkIn='2026-11-18'; checkOut='2026-11-21' } } } | ConvertTo-Json -Depth 5 -Compress
    try { $response = Invoke-WebRequest 'http://127.0.0.1:5057/mcp/' -Method Post -Headers $headers -ContentType 'application/json' -Body $body -UseBasicParsing -TimeoutSec 20 }
    catch { throw 'Desktop MCP request failed; response details suppressed.' }
    $json = (@($response.Content -split "`n" | Where-Object { $_.StartsWith('data: ') })[0]).Substring(6) | ConvertFrom-Json
    if (-not $json.result.structuredContent -or $json.result.isError) { throw 'Desktop MCP did not return SQL availability.' }
    Write-Output "PASS: Project_HMS.exe (PID $($app.Id)) served GetAvailableRooms from the live database."
    # Process.MainWindowHandle omits hidden windows, so address only this PID's titled login.
    if (-not [HotelDemoWindow]::Close($app) -or -not $app.WaitForExit(10000)) { throw 'Desktop did not close gracefully.' }
    $socket = [Net.Sockets.TcpClient]::new()
    try {
        try { $socket.Connect('127.0.0.1',5057) } catch [Net.Sockets.SocketException] { }
        if ($socket.Connected) { throw 'MCP port remained open after closing the desktop.' }
    } finally { $socket.Dispose() }
    Write-Output 'PASS: closing WinForms stopped its embedded MCP endpoint.'
} finally {
    if ($app -and -not $app.HasExited) { Stop-Process -Id $app.Id; $app.WaitForExit() }
    if ($app) { $app.Dispose() }
    $env:HMS_MCP_TOKEN = $oldToken; $env:HMS_MCP_ENABLED = $oldEnabled
    $env:HMS_CONNECTION_STRING = $oldConnection
}
