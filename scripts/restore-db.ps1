param(
    [string]$Host = "localhost",
    [int]$Port = 5432,
    [string]$Database = "Hr_PayRoll",
    [string]$Username = "postgres",
    [string]$Password = "1992",
    [Parameter(Mandatory = $true)]
    [string]$BackupFile
)

$ErrorActionPreference = "Stop"

function Resolve-ToolPath {
    param(
        [string]$ToolName,
        [string[]]$Candidates
    )

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($path in $Candidates) {
        if (Test-Path $path) {
            return $path
        }
    }

    throw "$ToolName not found. Add PostgreSQL bin folder to PATH or install PostgreSQL client tools."
}

if (!(Test-Path $BackupFile)) {
    throw "Backup file not found: $BackupFile"
}

$pgRestore = Resolve-ToolPath -ToolName "pg_restore" -Candidates @(
    "C:\Program Files\PostgreSQL\18\bin\pg_restore.exe",
    "C:\Program Files\PostgreSQL\17\bin\pg_restore.exe",
    "C:\Program Files\PostgreSQL\16\bin\pg_restore.exe",
    "C:\Program Files\PostgreSQL\15\bin\pg_restore.exe",
    "C:\Program Files\PostgreSQL\14\bin\pg_restore.exe"
)

$psql = Resolve-ToolPath -ToolName "psql" -Candidates @(
    "C:\Program Files\PostgreSQL\18\bin\psql.exe",
    "C:\Program Files\PostgreSQL\17\bin\psql.exe",
    "C:\Program Files\PostgreSQL\16\bin\psql.exe",
    "C:\Program Files\PostgreSQL\15\bin\psql.exe",
    "C:\Program Files\PostgreSQL\14\bin\psql.exe"
)

$extension = [System.IO.Path]::GetExtension($BackupFile).ToLowerInvariant()
$env:PGPASSWORD = $Password
try {
    if ($extension -eq ".sql") {
        & $psql -h $Host -p $Port -U $Username -d $Database -f $BackupFile
    }
    else {
        & $pgRestore -h $Host -p $Port -U $Username -d $Database --clean --if-exists $BackupFile
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed with exit code $LASTEXITCODE."
    }

    Write-Host "Restore completed from: $BackupFile"
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
