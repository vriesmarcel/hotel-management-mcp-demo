param([Parameter(Mandatory=$true)][string]$SourceConfig)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
[xml]$local=Get-Content -LiteralPath $SourceConfig -Raw
$builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder $local.connectionStrings.add.connectionString
if ($builder.InitialCatalog -ne 'Project_HMS') { throw 'The source must be the original Project_HMS database.' }
$database='Project_HMS_Demo03_'+[guid]::NewGuid().ToString('N').Substring(0,8)
$source=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$target=$null
function Execute($connection,$sql) {
    $command=$connection.CreateCommand(); $command.CommandText=$sql; $command.CommandTimeout=60
    try { [void]$command.ExecuteNonQuery() } finally { $command.Dispose() }
}
function Read($connection,$sql) {
    $command=$connection.CreateCommand(); $command.CommandText=$sql
    $adapter=New-Object System.Data.SqlClient.SqlDataAdapter $command; $table=New-Object System.Data.DataTable
    try { [void]$adapter.Fill($table); return ,$table } finally { $adapter.Dispose(); $command.Dispose() }
}
function Fingerprint($connection) {
    return (@('Room','Booking','FoodPackage','ServicePackage','Login') | ForEach-Object {
        $r=Read $connection "SELECT COUNT_BIG(*) AS N,CHECKSUM_AGG(BINARY_CHECKSUM(*)) AS H FROM dbo.[$_];"
        "$_/$($r.Rows[0].N)/$($r.Rows[0].H)"
    }) -join '|'
}
try {
    $source.Open(); $before=Fingerprint $source
    $keys=Read $source "SELECT t.name AS TableName,c.name AS ColumnName FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_primary_key=1 ORDER BY t.name,ic.key_ordinal;"
    if ($keys.Rows.Count -ne 5 -or @($keys.Rows|Group-Object TableName).Count -ne 5) { throw 'Expected five single-column primary keys in the original schema.' }
    $foreign=Read $source "SELECT OBJECT_NAME(fc.parent_object_id) AS ParentTable,pc.name AS ParentColumn,OBJECT_NAME(fc.referenced_object_id) AS RefTable,rc.name AS RefColumn FROM sys.foreign_key_columns fc JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id;"
    Execute $source "CREATE DATABASE [$database];"
    $builder['Initial Catalog']=$database; $builder['TrustServerCertificate']=$true
    $target=New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString; $target.Open()
    Execute $target "EXEC sys.sp_addextendedproperty @name=N'HotelDemo03',@value=N'Synthetic rehearsal data';"
    foreach ($table in @('Room','Booking','FoodPackage','ServicePackage','Login')) {
        Execute $target "SELECT TOP (0) * INTO dbo.[$table] FROM Project_HMS.dbo.[$table];"
    }
    foreach ($key in $keys.Rows) { Execute $target "ALTER TABLE dbo.[$($key.TableName)] ADD PRIMARY KEY ([$($key.ColumnName)]);" }
    foreach ($key in $foreign.Rows) {
        Execute $target "ALTER TABLE dbo.[$($key.ParentTable)] ADD FOREIGN KEY ([$($key.ParentColumn)]) REFERENCES dbo.[$($key.RefTable)] ([$($key.RefColumn)]);"
    }
    Execute $target @'
INSERT dbo.FoodPackage(FId,FName,FPackageCost) SELECT FId,FName,FPackageCost FROM Project_HMS.dbo.FoodPackage;
INSERT dbo.ServicePackage(SId,SName,SPackageCost) SELECT SId,SName,SPackageCost FROM Project_HMS.dbo.ServicePackage;
INSERT dbo.Room(RId,Category,IsBooked,RoomCost,BId) SELECT RId,Category,N'No',RoomCost,NULL FROM Project_HMS.dbo.Room;
IF NOT EXISTS(SELECT 1 FROM dbo.FoodPackage WHERE FId=0) INSERT dbo.FoodPackage VALUES(0,N'None',0);
IF NOT EXISTS(SELECT 1 FROM dbo.ServicePackage WHERE SId=0) INSERT dbo.ServicePackage VALUES(0,N'None',0);
INSERT dbo.Login(UId,Username,Password,Role) VALUES(1,N'demo-manager',N'123456',N'Manager');
'@
    foreach ($migration in @('001-id-sequences.sql','002-booking-workflows.sql')) { Execute $target (Get-Content (Join-Path $root "database/$migration") -Raw) }
    if ((Fingerprint $source) -ne $before) { throw 'Source fingerprint changed; check for external writers.' }
    $desktop=[xml]'<connectionStrings><add name="HotelDatabase" providerName="Microsoft.Data.SqlClient" /></connectionStrings>'
    $desktop.connectionStrings.add.SetAttribute('connectionString',$builder.ConnectionString)
    $desktop.Save((Join-Path $root 'Project_HMS/ConnectionStrings.local.config'))
    @{ConnectionStrings=@{HotelDatabase=$builder.ConnectionString}}|ConvertTo-Json|Set-Content (Join-Path $root 'Hotel.Mcp.Host/appsettings.Local.json') -Encoding utf8
    Write-Output "Created $database with the original room/package catalog and no original guests or logins."
    Write-Output 'Both applications are configured. Desktop demo login: demo-manager / 123456. Original database fingerprint unchanged.'
    Write-Output 'Rerunning creates a fresh database and switches both local configurations; it never drops an existing database.'
} finally {
    if ($target) { $target.Dispose() }; $source.Dispose(); $builder.Clear()
}
