param([string]$SourceConfig, [string]$ContainerName, [string]$Server='localhost,1433', [string]$Configuration='Debug')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location (Join-Path $root 'Hotel.Mcp.Host/Ui')
try {
    & npm.cmd ci --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw 'UI dependency installation failed.' }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'UI bundling failed.' }
} finally { Pop-Location }
& (Join-Path $PSScriptRoot 'Test-Demo03.ps1') -SourceConfig $SourceConfig -ContainerName $ContainerName -Server $Server -Configuration $Configuration -IncludeMcpUi
