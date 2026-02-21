param(
    [string]$TaskName = "KsaHrPayroll-Postgres-Backup",
    [string]$RunAt = "02:00",
    [string]$Host = "localhost",
    [int]$Port = 5432,
    [string]$Database = "Hr_PayRoll",
    [string]$Username = "postgres",
    [string]$Password = "1992",
    [string]$OutputDir = ".\backups\daily"
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "backup-db.ps1"
if (!(Test-Path $scriptPath)) {
    throw "backup-db.ps1 was not found beside this script."
}

$absoluteOutputDir = Resolve-Path (Split-Path -Parent (Join-Path $PSScriptRoot $OutputDir)) -ErrorAction SilentlyContinue
if (-not $absoluteOutputDir) {
    $fullOutputDir = Join-Path $PSScriptRoot $OutputDir
    New-Item -ItemType Directory -Path $fullOutputDir -Force | Out-Null
    $fullOutputDir = (Resolve-Path $fullOutputDir).Path
}
else {
    $fullOutputDir = Join-Path $absoluteOutputDir.Path (Split-Path -Leaf $OutputDir)
    New-Item -ItemType Directory -Path $fullOutputDir -Force | Out-Null
}

$taskActionArgs = @(
    "-ExecutionPolicy", "Bypass",
    "-File", "`"$scriptPath`"",
    "-Host", "`"$Host`"",
    "-Port", "$Port",
    "-Database", "`"$Database`"",
    "-Username", "`"$Username`"",
    "-Password", "`"$Password`"",
    "-OutputDir", "`"$fullOutputDir`""
) -join " "

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $taskActionArgs
$trigger = New-ScheduledTaskTrigger -Daily -At $RunAt
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description "Daily PostgreSQL backup for KSA HR Payroll SaaS." -Force | Out-Null

Write-Host "Scheduled task created/updated: $TaskName"
Write-Host "Run time: $RunAt"
Write-Host "Output folder: $fullOutputDir"
