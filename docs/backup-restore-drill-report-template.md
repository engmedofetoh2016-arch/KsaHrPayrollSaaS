# Backup and Restore Drill Report Template

Date:
Drill ID:
Environment: Staging
Owner:

## 1) Drill Summary

- Start time (UTC):
- End time (UTC):
- Duration:
- Result: Pass / Fail

## 2) Recovery Metrics

- Target RTO: 60 min
- Actual RTO:
- Target RPO: 15 min
- Actual RPO:

## 3) Backup and Restore Artifacts

- Backup ID:
- Backup timestamp (UTC):
- Restore job ID:
- Restore completion timestamp (UTC):
- Storage location:

## 4) Data Integrity Validation

| Check | Expected | Actual | Pass/Fail |
|---|---|---|---|
| Latest migration present | Yes |  |  |
| PayrollRunSet row count |  |  |  |
| PayrollLineSet row count |  |  |  |
| ExportArtifactSet row count |  |  |  |
| AuditLogSet row count |  |  |  |
| ComplianceAlertSet row count |  |  |  |

## 5) Application Smoke Tests

| Test | Result | Notes |
|---|---|---|
| API starts successfully |  |  |
| Login endpoint works |  |  |
| Payroll runs listing works |  |  |
| WPS/GOSI export listing works |  |  |
| Audit logs endpoint works |  |  |

## 6) Compliance Traceability Validation

- `PAYROLL_EXPORT_REQUEST` records restored: Yes / No
- Export artifacts downloadable after restore: Yes / No
- Rejection reason metadata available: Yes / No

## 7) Findings and Actions

1.
2.
3.

## 8) Signoff

- DevOps name/sign/date:
- QA name/sign/date:
- Engineering manager sign/date:
