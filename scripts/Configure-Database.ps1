param([string]$Server = 'localhost,1433', [string]$UserName = 'sa', [switch]$Force)
$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path $PSScriptRoot -Parent) 'Project_HMS\ConnectionStrings.local.config'
if ((Test-Path -LiteralPath $path) -and -not $Force) { Write-Output 'Local database configuration already exists.'; return }
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
try {
    if ($env:HMS_CONNECTION_STRING) { $builder.set_ConnectionString($env:HMS_CONNECTION_STRING) }
    else {
        $password = Read-Host "SQL password for $UserName at $Server" -AsSecureString
        try {
            $builder['Data Source'] = $Server; $builder['Initial Catalog'] = 'Project_HMS'
            $builder['User ID'] = $UserName; $builder['TrustServerCertificate'] = $true
            $builder['Password'] = ([Net.NetworkCredential]::new('', $password)).Password
        } finally { $password.Dispose() }
    }
    $xml = [xml]'<connectionStrings><add name="HotelDatabase" providerName="System.Data.SqlClient" /></connectionStrings>'
    $xml.connectionStrings.add.SetAttribute('connectionString', $builder.ConnectionString)
    $xml.Save($path)
    Write-Output 'Saved database settings to the Git-ignored ConnectionStrings.local.config. You can now use F5 normally.'
} finally { $builder.Clear() }
