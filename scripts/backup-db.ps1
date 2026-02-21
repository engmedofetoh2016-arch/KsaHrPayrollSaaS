param(
    [string]$Host = "localhost",
    [int]$Port = 5432,
    [string]$Database = "Hr_PayRoll",
    [string]$Username = "postgres",
    [string]$Password = "1992",
    [string]$OutputDir = ".\backups",
    [switch]$PlainSql
)

$ErrorActionPreference = "Stop"

function Resolve-PgDumpPath {
    $candidate = Get-Command pg_dump -ErrorAction SilentlyContinue
    if ($candidate) {
        return $candidate.Source
    }

    $windowsCandidates = @(
        "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe",
        "C:\Program Files\PostgreSQL\17\bin\pg_dump.exe",
        "C:\Program Files\PostgreSQL\16\bin\pg_dump.exe",
        "C:\Program Files\PostgreSQL\15\bin\pg_dump.exe",
        "C:\Program Files\PostgreSQL\14\bin\pg_dump.exe"
    )

    foreach ($path in $windowsCandidates) {
        if (Test-Path $path) {
            return $path
        }
    }

    throw "pg_dump not found. Add PostgreSQL bin folder to PATH or install PostgreSQL client tools."
}

if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$extension = if ($PlainSql) { "sql" } else { "dump" }
$fileName = "$($Database)-$timestamp.$extension"
$outputFile = Join-Path $OutputDir $fileName
$pgDump = Resolve-PgDumpPath

$env:PGPASSWORD = $Password
try {
    if ($PlainSql) {
        & $pgDump -h $Host -p $Port -U $Username -d $Database -f $outputFile
    }
    else {
        & $pgDump -h $Host -p $Port -U $Username -d $Database -F c -f $outputFile
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Backup failed with exit code $LASTEXITCODE."
    }

    Write-Host "Backup completed: $outputFile"
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
