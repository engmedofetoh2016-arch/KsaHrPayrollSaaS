# Backup and Restore Drill Runbook (Phase 1 MVP)

Date: 2026-02-27
Owner: DevOps
Participants: DevOps, QA, Backend on-call

## Purpose

Validate that payroll data and compliance evidence can be restored within agreed recovery targets.

## Scope

- Primary application database
- Key tables:
  - `PayrollRunSet`
  - `PayrollLineSet`
  - `ExportArtifactSet`
  - `AuditLogSet`
  - `ComplianceAlertSet`
  - `__EFMigrationsHistory`

## Recovery Targets

- RTO (Recovery Time Objective): 60 minutes
- RPO (Recovery Point Objective): 15 minutes

## Preconditions

1. Staging environment is healthy.
2. Backup storage is reachable and access-controlled.
3. Latest backup and one older backup are available.
4. A drill ticket is created with planned start/end window.

## Procedure

1. Capture baseline
- Record staging DB size, timestamp, and active migration ID.
- Capture counts for:
  - payroll runs
  - payroll lines
  - export artifacts
  - audit logs

2. Take fresh backup snapshot
- Create backup with immutable tag:
  - `phase1-drill-YYYYMMDD-HHMM`
- Record backup ID and storage location.

3. Simulate data-loss scenario
- In staging only, remove or alter non-production test records.
- Record exact commands and timestamps.

4. Restore from backup
- Restore DB to new staging restore target.
- Run integrity checks:
  - migration history present
  - required tables present
  - row counts consistent with baseline backup point

5. Application verification
- Start API and worker against restored DB.
- Execute smoke checks:
  - login works
  - payroll runs list endpoint works
  - WPS/GOSI export list endpoint works
  - audit logs endpoint works

6. Compliance verification
- Confirm restored `ExportArtifactSet` includes historical artifacts.
- Confirm restored `AuditLogSet` includes `PAYROLL_EXPORT_REQUEST` entries.

7. Measure and record
- Compute actual RTO and RPO from timestamps.
- Mark pass/fail against targets.

## Acceptance Criteria

1. Restore completes successfully in staging.
2. RTO <= 60 minutes.
3. RPO <= 15 minutes.
4. All scoped tables are queryable and consistent.
5. QA confirms critical payroll/compliance flows are operational.

## Evidence Required

- Drill ticket link
- Backup ID and restore job ID
- Command transcript or execution logs
- Before/after row-count table
- API smoke test results
- Signed drill report:
  - `docs/backup-restore-drill-report-template.md`

## Rollback

If drill restore fails:

1. Stop verification and preserve failure logs.
2. Repoint staging to last known-good snapshot.
3. Open incident and assign remediation owner.
4. Schedule re-drill within 5 business days.
