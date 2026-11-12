param([string]$Configuration='Debug', [switch]$IncludeMcpUi)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (-not $env:HMS_TEST_CONNECTION_STRING -or -not $env:HMS_TEST_DATABASE_MARKER) { throw 'Run through Test-Demo03.ps1 with an owned test database.' }
$cs=New-Object System.Data.SqlClient.SqlConnectionStringBuilder $env:HMS_TEST_CONNECTION_STRING
if (-not $cs.InitialCatalog.StartsWith('Project_HMS_RefactorTest_')) { throw 'Test database required.' }
$sql=New-Object System.Data.SqlClient.SqlConnection $cs.ConnectionString
try {
    $sql.Open(); $command=$sql.CreateCommand()
    $command.CommandText="SELECT CONVERT(nvarchar(100),value) FROM sys.extended_properties WHERE class=0 AND name=N'HotelRefactoringTest';"
    if ($command.ExecuteScalar() -ne $env:HMS_TEST_DATABASE_MARKER) { throw 'Test ownership mismatch.' }
} finally { $sql.Dispose() }
$oldConnection=$env:HMS_CONNECTION_STRING; $oldPort=$env:HMS_MCP_PORT
$env:HMS_CONNECTION_STRING=$env:HMS_TEST_CONNECTION_STRING; $env:HMS_MCP_PORT='5059'
$hostProcess=$null
$log=Join-Path ([IO.Path]::GetTempPath()) ('hotel-mcp-test-'+[guid]::NewGuid().ToString('N'))
$script:checks=0; $script:request=0
function Assert($condition,$message) { if (-not $condition) { throw $message }; $script:checks++ }
function Rpc($context,$method,$parameters) {
    $script:request++
    $body=@{jsonrpc='2.0';id=$script:request;method=$method;params=$parameters}|ConvertTo-Json -Depth 30 -Compress
    $response=Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:5059/mcp/$context" -Method Post -ContentType 'application/json' -Headers @{Authorization='Bearer 123456';Accept='application/json, text/event-stream';'MCP-Protocol-Version'='2025-11-25'} -Body $body -TimeoutSec 15
    $text=$response.Content
    if ($text -is [byte[]]) { $text=[Text.Encoding]::UTF8.GetString($text) }
    if ($text.StartsWith('event:') -or $text.StartsWith('data:')) { $text=($text -split "`n" | Where-Object { $_.StartsWith('data:') } | Select-Object -Last 1).Substring(5).Trim() }
    return $text|ConvertFrom-Json
}
function Call($context,$name,$arguments) {
    $r=Rpc $context 'tools/call' @{name=$name;arguments=$arguments}
    Assert (-not $r.error -and -not $r.result.isError) "Tool $name failed: $($r|ConvertTo-Json -Depth 8 -Compress)"
    if ($r.result.structuredContent) { return $r.result.structuredContent }
    return $r.result.content[0].text|ConvertFrom-Json
}
try {
    if (Get-NetTCPConnection -LocalPort 5059 -State Listen -ErrorAction SilentlyContinue) { throw 'Test port 5059 is already in use; no existing process was stopped.' }
    $hostProcess=Start-Process dotnet -ArgumentList @((Join-Path $root "Hotel.Mcp.Host/bin/$Configuration/net10.0/Hotel.Mcp.Host.dll")) -WorkingDirectory (Join-Path $root 'Hotel.Mcp.Host') -WindowStyle Hidden -PassThru -RedirectStandardOutput ($log+'.out') -RedirectStandardError ($log+'.err')
    for ($i=0;$i -lt 60;$i++) {
        if ($hostProcess.HasExited) { throw 'Owned MCP test host exited before listening.' }
        if (Get-NetTCPConnection -LocalPort 5059 -State Listen -ErrorAction SilentlyContinue) { break }
        Start-Sleep -Milliseconds 250
    }
    foreach ($context in @('reservations','stays','billing')) {
        $init=Rpc $context 'initialize' @{protocolVersion='2025-11-25';capabilities=@{};clientInfo=@{name='hotel-demo-tests';version='1'}}
        Assert ($init.result.serverInfo.name -eq "hotel-$context") "Wrong server identity for $context."
        $list=Rpc $context 'tools/list' @{}
        $expected=switch($context) { 'reservations' {@('GetAvailableRooms','GetPackages','CalculateQuote','CreateReservation','GetReservation')} 'stays' {@('GetArrivals','CheckIn')} 'billing' {@('GetBill','RecordPayment','CompleteCheckout')} }
        Assert (-not (Compare-Object ($list.result.tools.name|Sort-Object) ($expected|Sort-Object))) "Catalog leaked tools for $context."
    }
    foreach ($pair in @(@('reservations','RecordPayment'),@('stays','CreateReservation'),@('billing','GetAvailableRooms'))) {
        $bad=Rpc $pair[0] 'tools/call' @{name=$pair[1];arguments=@{}}
        Assert ($bad.error -or $bad.result.isError) 'Cross-context invocation must fail.'
    }
    $resources=Rpc 'reservations' 'resources/list' @{}
    Assert ($resources.result.resources.uri -contains 'hotel://reservation-policy') 'Reservation policy missing.'
    Assert ($resources.result.resources.uri -contains 'ui://hotel/reservations/v1') 'MCP Apps resource missing.'
    foreach ($other in @('stays','billing')) {
        $leak=Rpc $other 'resources/read' @{uri='ui://hotel/reservations/v1'}
        Assert ($leak.error -or -not $leak.result.contents) 'Reservation UI leaked to another context.'
    }
    $policy=Rpc 'reservations' 'resources/read' @{uri='hotel://reservation-policy'}
    Assert ($policy.result.contents[0].text -match '15 minutes') 'Policy unreadable.'
    $prompt=Rpc 'reservations' 'prompts/get' @{name='PlanReservation';arguments=@{requirements='A room for three nights'}}
    Assert ($prompt.result.messages.Count -gt 0) 'Planning prompt unavailable.'
    $badResource=Rpc 'billing' 'resources/read' @{uri='hotel://reservation-policy'}
    Assert ($badResource.error -or -not $badResource.result.contents) 'Resource must not cross contexts.'
    $today=(Get-Date).ToString('yyyy-MM-dd'); $end=(Get-Date).AddDays(3).ToString('yyyy-MM-dd')
    $available=Call 'reservations' 'GetAvailableRooms' @{checkIn=$today;checkOut=$end}
    Assert ($available.rooms.Count -gt 0 -and $available.nights -eq 3) 'Availability projection invalid.'
    $packages=Call 'reservations' 'GetPackages' @{}
    $q=Call 'reservations' 'CalculateQuote' @{roomId=$available.rooms[0].roomId;checkIn=$today;checkOut=$end;foodPackageId=$packages.food[0].fId;servicePackageId=$packages.services[0].sId}
    $args=@{quoteId=$q.quoteId;guestName='HTTP synthetic guest';address='Demo';phone='Demo';nationalId='DEMO-HTTP';advance=0;requestId=[guid]::NewGuid().ToString();confirmed=$true}
    $booking=Call 'reservations' 'CreateReservation' $args
    $retry=Call 'reservations' 'CreateReservation' $args
    Assert ($booking.reservationId -eq $retry.reservationId) 'HTTP booking retry duplicated reservation.'
    Assert (-not ($booking|ConvertTo-Json -Compress).Contains('DEMO-HTTP')) 'Identity data leaked into summary.'
    $checked=Call 'stays' 'CheckIn' @{reservationId=$booking.reservationId;requestId=[guid]::NewGuid().ToString();confirmed=$true}
    Assert ($checked.status -eq 'CheckedIn') 'HTTP check-in failed.'
    $pay=@{reservationId=$booking.reservationId;amount=1;requestId=[guid]::NewGuid().ToString();confirmed=$true}
    $paid=Call 'billing' 'RecordPayment' $pay; $paidAgain=Call 'billing' 'RecordPayment' $pay
    Assert ($paid.remaining -eq $paidAgain.remaining -and $paid.paid -eq 1) 'HTTP payment retry duplicated payment.'
    $done=Call 'billing' 'CompleteCheckout' @{reservationId=$booking.reservationId;paidNow=$paid.remaining;requestId=[guid]::NewGuid().ToString();confirmed=$true}
    Assert ($done.reservation.status -eq 'CheckedOut' -and $done.reservation.remaining -eq 0) 'HTTP checkout failed.'
    foreach ($test in @(@{Authorization='Bearer wrong'},@{Authorization='Bearer 123456';Origin='https://untrusted.example'})) {
        try { Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:5059/mcp/reservations' -Headers $test -ErrorAction Stop|Out-Null; throw 'Unexpected HTTP access.' }
        catch { Assert ([int]$_.Exception.Response.StatusCode -in @(401,403)) 'Invalid token/origin was not rejected.' }
    }
    Write-Output "PASS: $script:checks MCP HTTP assertions; three isolated catalogs, resources/prompts, booking/check-in/payment/checkout, safe retries, token and Origin checks."
    if ($IncludeMcpUi) {
        # The preceding empty-table/desktop tests intentionally remove package 0.
        # Restore the demo's explicit no-package options in this owned fixture only.
        $fixture=New-Object System.Data.SqlClient.SqlConnection $cs.ConnectionString
        try {
            $fixture.Open(); $seed=$fixture.CreateCommand()
            $seed.CommandText="IF NOT EXISTS (SELECT 1 FROM dbo.FoodPackage WHERE FId=0) INSERT dbo.FoodPackage (FId,FName,FPackageCost) VALUES (0,N'',0); IF NOT EXISTS (SELECT 1 FROM dbo.ServicePackage WHERE SId=0) INSERT dbo.ServicePackage (SId,SName,SPackageCost) VALUES (0,N'',0);"
            [void]$seed.ExecuteNonQuery(); $seed.Dispose()
        } finally { $fixture.Dispose() }
        $oldUiTest=$env:HMS_UI_TEST_OWNED
        try {
            $env:HMS_UI_TEST_OWNED='5059'
            Push-Location (Join-Path $root 'Hotel.Mcp.Host/Ui')
            try { & npm.cmd test; if ($LASTEXITCODE -ne 0) { throw 'MCP Apps browser tests failed.' } }
            finally { Pop-Location }
        } finally { $env:HMS_UI_TEST_OWNED=$oldUiTest }
    }
} finally {
    if ($hostProcess -and -not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id; $hostProcess.WaitForExit() }
    $env:HMS_CONNECTION_STRING=$oldConnection; $env:HMS_MCP_PORT=$oldPort
    foreach($path in @($log+'.out',$log+'.err')) { if(Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path } }
}
