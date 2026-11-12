param([string]$HostSettings)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if (-not $HostSettings) { $HostSettings=Join-Path $root 'Hotel.Mcp.Host/appsettings.Local.json' }
$settings=Get-Content -LiteralPath $HostSettings -Raw | ConvertFrom-Json
$builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder $settings.ConnectionStrings.HotelDatabase
if (-not $builder.InitialCatalog.StartsWith('Project_HMS_Demo03_')) { throw 'Only a synthetic Demo03 rehearsal database can be upgraded by this script.' }
$connection=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
try {
 $connection.Open(); $command=$connection.CreateCommand()
 $command.CommandText="SELECT CONVERT(nvarchar(100),value) FROM sys.extended_properties WHERE class=0 AND name=N'HotelDemo03';"
 if ($command.ExecuteScalar() -ne 'Synthetic rehearsal data') { throw 'Demo database ownership marker missing.' }
 $command.CommandText=Get-Content (Join-Path $root 'database/003-reservation-terms.sql') -Raw
 [void]$command.ExecuteNonQuery()
 Write-Output 'Applied additive reservation-terms migration. Existing bookings and quotes were preserved; old quotes must be recalculated.'
} finally { $connection.Dispose(); $builder.Clear() }
