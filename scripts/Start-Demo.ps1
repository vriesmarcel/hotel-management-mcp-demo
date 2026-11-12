param([string]$Server = 'localhost,1433', [string]$UserName = 'sa', [switch]$NoBuild, [switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Configure-Database.ps1') -Server $Server -UserName $UserName
if (-not $NoBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'Install Visual Studio with .NET Framework 4.8 desktop development support.' }
    & $msbuild (Join-Path $root 'Project_HMS.sln') /restore /nologo /v:minimal /p:Configuration=Debug
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
Write-Output 'MCP: http://127.0.0.1:5057/mcp/ — fixed demo token 123456; no MCP environment variables required.'
if (-not $PrepareOnly) {
    $exe = Join-Path $root 'Project_HMS\bin\Debug\Project_HMS.exe'
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Normal | Out-Null
}
