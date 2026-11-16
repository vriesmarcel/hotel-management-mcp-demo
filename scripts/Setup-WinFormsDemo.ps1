param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$python = (Get-Command python -ErrorAction Stop).Source
& $python -c 'import sys; assert sys.version_info >= (3,11), "Python 3.11+ required"'
if ($LASTEXITCODE -ne 0) { throw 'Python 3.11+ is required.' }
if (-not (Test-Path (Join-Path $root 'Project_HMS/ConnectionStrings.local.config'))) {
    throw 'Run scripts/Configure-Database.ps1 first, then rerun setup. SQL credentials will not be printed.'
}
if (-not $SkipBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'Visual Studio MSBuild was not found.' }
    & $msbuild (Join-Path $root 'Project_HMS/Project_HMS.csproj') /restore /nologo /v:minimal /p:Configuration=Debug
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Stop a running Project_HMS debugger/process before rebuilding.' }
}
& $python (Join-Path $PSScriptRoot 'Configure-WinFormsCodex.py')
if ($LASTEXITCODE -ne 0) { throw 'Codex configuration setup failed.' }
Write-Output 'NEXT: Open Project_HMS.sln in this worktree; set Project_HMS as startup project; press F5.'
Write-Output 'Breakpoint: Project_HMS/Mcp/AvailabilityTools.cs, var arrival = ParseDate(checkIn).'
Write-Output 'Then: python scripts/Configure-WinFormsCodex.py --probe'
Write-Output 'Restart Codex MCP servers (or fully relaunch Codex) AFTER the WinForms login window appears.'
Write-Output 'Start a fresh chat and ask GetAvailableRooms for November 18-21, 2026. Do not book.'
