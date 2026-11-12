param([switch]$NoBuild, [switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $root 'Hotel.Mcp.Host/appsettings.Local.json')) -or -not (Test-Path (Join-Path $root 'Project_HMS/ConnectionStrings.local.config'))) {
    throw 'Run Initialize-Demo03.ps1 -SourceConfig <the Demo 02 local configuration> first.'
}
if (-not $NoBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'Install Visual Studio with .NET Framework 4.8 desktop development support.' }
    & $msbuild (Join-Path $root 'Project_HMS.sln') /restore /nologo /v:minimal /p:Configuration=Debug
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
if (-not $PrepareOnly) {
    if (Get-NetTCPConnection -LocalPort 5057 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 5057 is in use. Stop the Demo 02 application or earlier host first.' }
    $logs = Join-Path $root '.demo-logs'
    [void](New-Item -ItemType Directory -Force -Path $logs)
    $hostProcess = Start-Process dotnet -ArgumentList @((Join-Path $root 'Hotel.Mcp.Host/bin/Debug/net10.0/Hotel.Mcp.Host.dll')) -WorkingDirectory (Join-Path $root 'Hotel.Mcp.Host') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs 'host.out.log') -RedirectStandardError (Join-Path $logs 'host.err.log')
    $exe = Join-Path $root 'Project_HMS\bin\Debug\Project_HMS.exe'
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Normal | Out-Null
    Write-Output "MCP host PID: $($hostProcess.Id). Closing WinForms leaves it running. Stop it with Stop-Process -Id $($hostProcess.Id)."
}
Write-Output 'Endpoints: http://127.0.0.1:5057/mcp/reservations, /mcp/stays, /mcp/billing. Fixed demo token: 123456.'
