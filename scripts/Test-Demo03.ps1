param(
    [string]$ContainerName,
    [string]$Server = 'localhost,1433',
    [string]$Configuration = 'Debug',
    [string]$SourceConfig,
    [switch]$IncludeMcpUi
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild with the .NET Framework 4.8 development workload is required.' }
& $msbuild (Join-Path $root 'Project_HMS.sln') /restore /nologo /verbosity:minimal "/p:Configuration=$Configuration"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$forms = Get-ChildItem (Join-Path $root 'Project_HMS') -Filter '*.cs' | Where-Object { $_.Name -notin @('Program.cs', 'DesktopServices.cs') }
foreach ($file in $forms) {
    $content = Get-Content $file.FullName -Raw
    if ($content -match 'DataAccess|System\.Data|Hotel\.(Sql|Application)|\b(SELECT\s+.+\s+FROM|INSERT\s+INTO|UPDATE\s+\w+\s+SET|DELETE\s+FROM)\b') {
        throw "Persistence boundary violation in $($file.Name)."
    }
}

$testExe = Join-Path $root "Hotel.Tests\bin\$Configuration\Hotel.Tests.exe"
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Application/boundary tests failed.' }

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
if ($ContainerName) {
    $container = docker inspect $ContainerName | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the explicitly selected database container.' }
    $entry = $container[0].Config.Env | Where-Object { $_ -match '^(MSSQL_SA_PASSWORD|SA_PASSWORD)=' } | Select-Object -First 1
    if (-not $entry) { throw 'Configure HMS_CONNECTION_STRING securely; no SQL credential is available in the selected container.' }
    $builder['Data Source'] = $Server
    $builder['Initial Catalog'] = 'Project_HMS'
    $builder['User ID'] = 'sa'
    $builder['Password'] = $entry.Substring($entry.IndexOf('=') + 1)
    $builder['TrustServerCertificate'] = $true
    $entry = $null
    $container = $null
} elseif ($env:HMS_CONNECTION_STRING) {
    $builder.set_ConnectionString($env:HMS_CONNECTION_STRING)
 } elseif ($SourceConfig) {
    [xml]$local=Get-Content -LiteralPath $SourceConfig -Raw
    $builder.set_ConnectionString($local.connectionStrings.add.connectionString)
} else { throw 'Pass -SourceConfig pointing to the no-refactor demo ConnectionStrings.local.config, or set HMS_CONNECTION_STRING.' }

if ($builder.InitialCatalog -ne 'Project_HMS') { throw 'Schema source must be Project_HMS; it is never modified.' }

function Execute-Sql($Connection, [string]$Sql) {
    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 60
    $command.CommandText = $Sql
    try { [void]$command.ExecuteNonQuery() } finally { $command.Dispose() }
}
function Read-Sql($Connection, [string]$Sql) {
    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
    $table = New-Object System.Data.DataTable
    try { [void]$adapter.Fill($table); return ,$table } finally { $adapter.Dispose(); $command.Dispose() }
}
function Source-Fingerprint($Connection) {
    $values = @()
    foreach ($table in @('Room','Booking','FoodPackage','ServicePackage','Login')) {
        $result = Read-Sql $Connection "SELECT COUNT_BIG(*) AS N,CHECKSUM_AGG(BINARY_CHECKSUM(*)) AS H FROM dbo.[$table];"
        $values += "$table/$($result.Rows[0].N)/$($result.Rows[0].H)"
    }
    return $values -join '|'
}

$database = 'Project_HMS_RefactorTest_' + [Guid]::NewGuid().ToString('N')
$marker = [Guid]::NewGuid().ToString('N')
$source = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
$target = $null
$created = $false
$oldTestConnection = $env:HMS_TEST_CONNECTION_STRING
$oldMarker = $env:HMS_TEST_DATABASE_MARKER
try {
    $source.Open()
    $before = Source-Fingerprint $source
    $unsupported = Read-Sql $source 'SELECT COUNT(*) AS N FROM sys.triggers WHERE parent_class=1;'
    if ($unsupported.Rows[0].N -ne 0) { throw 'Live triggers require explicit schema-copy review before testing.' }
    $columns = Read-Sql $source "SELECT COUNT(*) AS N FROM sys.columns c JOIN sys.tables t ON c.object_id=t.object_id WHERE t.name IN ('Room','Booking','FoodPackage','ServicePackage','Login') AND (c.is_computed=1 OR c.default_object_id<>0 OR c.is_identity=1);"
    if ($columns.Rows[0].N -ne 0) { throw 'Computed/default/identity columns require explicit schema-copy review.' }
    $checks = Read-Sql $source 'SELECT COUNT(*) AS N FROM sys.check_constraints;'
    if ($checks.Rows[0].N -ne 0) { throw 'Check constraints require explicit schema-copy review.' }
    $uniqueIndexes = Read-Sql $source 'SELECT COUNT(*) AS N FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id WHERE i.is_unique=1 AND i.is_primary_key=0;'
    if ($uniqueIndexes.Rows[0].N -ne 0) { throw 'Additional unique indexes require explicit schema-copy review.' }
    $keys = Read-Sql $source "SELECT t.name AS TableName,i.name AS KeyName,c.name AS ColumnName FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.is_primary_key=1 ORDER BY t.name,ic.key_ordinal;"
    if ($keys.Rows.Count -ne 5 -or @($keys.Rows | Group-Object TableName).Count -ne 5) {
        throw 'Expected five single-column primary keys; review schema-copy logic for this database.'
    }
    $foreignKeys = Read-Sql $source "SELECT fk.name AS KeyName,OBJECT_NAME(fc.parent_object_id) AS ParentTable,pc.name AS ParentColumn,OBJECT_NAME(fc.referenced_object_id) AS RefTable,rc.name AS RefColumn FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=fk.object_id JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id;"
    Execute-Sql $source "CREATE DATABASE [$database];"
    $created = $true
    $builder['Initial Catalog'] = $database
    $builder['TrustServerCertificate'] = $true
    $target = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    $target.Open()
    Execute-Sql $target "EXEC sys.sp_addextendedproperty @name=N'HotelRefactoringTest',@value=N'$marker';"
    Execute-Sql $source "ALTER DATABASE [$database] SET COMPATIBILITY_LEVEL=110;"
    foreach ($table in @('Room','Booking','FoodPackage','ServicePackage','Login')) {
        Execute-Sql $target "SELECT TOP (0) * INTO dbo.[$table] FROM Project_HMS.dbo.[$table];"
    }
    foreach ($key in $keys.Rows) {
        Execute-Sql $target "ALTER TABLE dbo.[$($key.TableName)] ADD CONSTRAINT [$($key.KeyName)] PRIMARY KEY ([$($key.ColumnName)]);"
    }
    foreach ($key in $foreignKeys.Rows) {
        Execute-Sql $target "ALTER TABLE dbo.[$($key.ParentTable)] ADD CONSTRAINT [$($key.KeyName)] FOREIGN KEY ([$($key.ParentColumn)]) REFERENCES dbo.[$($key.RefTable)] ([$($key.RefColumn)]);"
    }
    Execute-Sql $target "INSERT dbo.FoodPackage (FId,FName,FPackageCost) VALUES (0,N'',0),(14,N'Synthetic food',20); INSERT dbo.ServicePackage (SId,SName,SPackageCost) VALUES (0,N'',0),(25,N'Synthetic service',5); INSERT dbo.Room (RId,Category,IsBooked,RoomCost,BId) VALUES (100,N'Synthetic room',N'No',100,NULL);"
    $migration = Get-Content (Join-Path $root 'database\001-id-sequences.sql') -Raw
    Execute-Sql $target $migration
    Execute-Sql $target $migration
    $workflowMigration = Get-Content (Join-Path $root 'database\002-booking-workflows.sql') -Raw
    Execute-Sql $target $workflowMigration
    Execute-Sql $target $workflowMigration
    $env:HMS_TEST_CONNECTION_STRING = $builder.ConnectionString
    $env:HMS_TEST_DATABASE_MARKER = $marker
    $testProcess = Start-Process -FilePath $testExe -ArgumentList '--integration' -NoNewWindow -PassThru
    if (-not $testProcess.WaitForExit(120000)) {
        Stop-Process -Id $testProcess.Id
        $testProcess.WaitForExit()
        throw 'Isolated SQL/desktop tests timed out; stopped only the owned test process.'
    }
    if ($testProcess.ExitCode -ne 0) { throw 'SQL/desktop integration tests failed.' }
    & (Join-Path $PSScriptRoot 'Test-ContextMcp.ps1') -Configuration $Configuration -IncludeMcpUi:$IncludeMcpUi
    if ((Source-Fingerprint $source) -ne $before) { throw 'Source database fingerprint changed during tests; investigate external writers.' }
    Write-Output 'PASS: original database row-count/checksum fingerprint unchanged; migration applied twice in isolated compatibility-level-110 schema copy.'
} finally {
    $env:HMS_TEST_CONNECTION_STRING = $oldTestConnection
    $env:HMS_TEST_DATABASE_MARKER = $oldMarker
    if ($target) { $target.Dispose() }
    if ($created -and $source.State -eq 'Open') {
        $ownership = Read-Sql $source "SELECT CONVERT(nvarchar(100),value) AS Marker FROM [$database].sys.extended_properties WHERE class=0 AND name=N'HotelRefactoringTest';"
        if ($ownership.Rows.Count -eq 1 -and $ownership.Rows[0].Marker -eq $marker) {
            [System.Data.SqlClient.SqlConnection]::ClearAllPools()
            Execute-Sql $source "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database];"
            Write-Output 'Removed only the owned, isolated test database.'
        } else {
            Write-Warning "Test database $database was not removed because ownership could not be verified."
        }
    }
    $source.Dispose()
    $builder.Clear()
}
