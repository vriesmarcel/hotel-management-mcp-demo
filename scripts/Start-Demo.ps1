param(
    [string]$Server = 'localhost,1433',
    [string]$UserName = 'sa',
    [switch]$NoBuild,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $NoBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'Install Visual Studio with .NET Framework 4.8 desktop development support.' }
    & $msbuild (Join-Path $root 'Project_HMS.sln') /restore /nologo /v:minimal /p:Configuration=Debug
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
if (-not $env:HMS_CONNECTION_STRING) {
    $password = Read-Host "SQL password for $UserName at $Server (kept in this shell only)" -AsSecureString
    try {
        $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
        $builder['Data Source'] = $Server
        $builder['Initial Catalog'] = 'Project_HMS'
        $builder['User ID'] = $UserName
        $builder['Password'] = ([Net.NetworkCredential]::new('', $password)).Password
        $builder['TrustServerCertificate'] = $true
        $env:HMS_CONNECTION_STRING = $builder.ConnectionString
    } finally { if ($builder) { $builder.Clear() }; $password.Dispose() }
}
$withMcp = Test-Path (Join-Path $root 'Hotel.Mcp')
if ($withMcp) {
    if (-not $env:HMS_MCP_TOKEN) {
        $bytes = New-Object byte[] 32
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($bytes); $env:HMS_MCP_TOKEN = [Convert]::ToBase64String($bytes) }
        finally { $random.Dispose(); [Array]::Clear($bytes,0,$bytes.Length) }
    }
    $env:HMS_MCP_ENABLED = '1'
    Write-Output 'MCP: http://127.0.0.1:5057/mcp/ — HMS_MCP_TOKEN is set in this shell; no secret is printed.'
    Write-Output 'For Codex, fully exit the desktop app and launch it from this shell to inherit HMS_MCP_TOKEN. See docs/demos/demo-02.md.'
} else {
    $env:HMS_MCP_ENABLED = $null
    Write-Output 'Demo 01: original direct-SQL WinForms application, without MCP.'
}
if (-not $PrepareOnly) {
    $exe = Join-Path $root 'Project_HMS\bin\Debug\Project_HMS.exe'
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Normal | Out-Null
}
