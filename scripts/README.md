# Demo Seed Script

Use this script to seed a full demo flow:
- Tenant + owner
- Employees (Saudi + non-Saudi mix with GOSI setup)
- Employee start dates (for EOS estimation)
- Employee WPS payment data (employee number + bank + IBAN)
- Iqama and work permit expiry sample data (for compliance alerts)
- Attendance inputs (varied overtime/absence)
- Approved unpaid leave (to show payroll leave deduction)
- Payroll period
- Employee allowances/deductions
- Payroll calculation with GOSI and unpaid leave deductions
- Data ready for EOS estimate endpoint
- Company profile preconfigured with WPS bank settings
- Optional export queue (register CSV, GOSI CSV, one payslip PDF)
- Optional approve+lock

## Run

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\seed-demo.ps1
```

Optional:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\seed-demo.ps1 -LockRun
```

Queue exports:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\seed-demo.ps1 -QueueExports
```

Custom API URL:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\seed-demo.ps1 -ApiBaseUrl http://localhost:5202
```

Combined example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\seed-demo.ps1 -ApiBaseUrl http://localhost:5202 -QueueExports -LockRun
```

# Database Backup / Restore

Use these scripts to protect production and pilot data.

## Backup

Default values match your current local setup (`Hr_PayRoll`, `postgres`, `1992`):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\backup-db.ps1
```

Backup to SQL text format:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\backup-db.ps1 -PlainSql
```

Custom output folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\backup-db.ps1 -OutputDir .\backups\daily
```

## Restore

Restore from `.dump` custom format:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\restore-db.ps1 -BackupFile .\backups\Hr_PayRoll-20260221-120000.dump
```

Restore from `.sql` file:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\restore-db.ps1 -BackupFile .\backups\Hr_PayRoll-20260221-120000.sql
```

## Note

Run restore against the correct target database/environment.  
Recommended safety: take a fresh backup immediately before any restore.

## Schedule Daily Backup (Windows)

Create/update a daily task at 2:00 AM:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\schedule-backup-task.ps1
```

Custom time and task name:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\schedule-backup-task.ps1 -TaskName "HrPayroll-Backup" -RunAt "01:30"
```
