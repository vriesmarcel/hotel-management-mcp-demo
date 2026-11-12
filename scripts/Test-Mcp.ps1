$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild (Join-Path $root 'tests\McpSmokeTests\McpSmokeTests.csproj') /restore /nologo /v:minimal /p:Configuration=Debug
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& (Join-Path $root 'tests\McpSmokeTests\bin\Debug\net48\McpSmokeTests.exe') (Join-Path $root 'Project_HMS\ConnectionStrings.local.config')
if ($LASTEXITCODE -ne 0) { throw 'MCP smoke tests failed.' }
& (Join-Path $PSScriptRoot 'Test-DesktopMcp.ps1')
