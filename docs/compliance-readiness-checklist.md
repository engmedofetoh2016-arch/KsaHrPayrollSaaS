# Compliance Readiness Checklist (WPS/GOSI + Security Baseline)

Date: 2026-02-27
Owner: Engineering

## Scope
- WPS export validation
- GOSI export validation
- Export traceability
- Security baseline evidence

## Implementation Status

1. Finalize WPS/GOSI file specs and validation rules
- Status: In Progress
- Implemented:
  - Pre-submission WPS validation (company and employee payment profile checks)
  - Pre-submission GOSI validation (wage base and contribution consistency checks)
  - Unified readiness endpoint: `/api/payroll/runs/{runId}/compliance-readiness`
- Remaining:
  - Formal signoff against official bank/regulator file specifications and sample cert pack.

2. Add pre-submission validators and rejection reason mapping
- Status: Done
- Evidence:
  - API blocks invalid WPS export requests with details.
  - API blocks invalid GOSI export requests with details.
  - Integration tests added in `src/backend/HrPayroll.Api.Tests/PayrollExportValidationTests.cs`.

3. Add compliance audit record for every export attempt
- Status: Done
- Evidence:
  - Audit method `PAYROLL_EXPORT_REQUEST` added for blocked and queued GOSI/WPS export requests.
  - Captures run path and attempt status.

4. Security baseline: secrets management, DB encryption-at-rest policy, backup/restore drill
- Status: Not Started
- Required evidence:
  - Secrets management policy doc + rotation cadence
  - DB encryption-at-rest control verification
  - Backup/restore drill runbook + drill report with timestamp and recovery duration

## Exit Criteria Tracking

1. WPS/GOSI exports pass internal validation suite
- Status: In Progress
- Evidence:
  - New automated integration tests:
    - WPS failure when payment data missing
    - WPS queue when payment data complete
    - GOSI failure when employee/run data mismatch
    - GOSI queue when data valid

2. Every export has full traceability (who, when, version, status)
- Status: In Progress
- Evidence:
  - Export request audit records present.
  - Export artifact status lifecycle present.
- Gap:
  - Add versioned compliance spec reference to export metadata (future enhancement).

3. Security checklist signed by DevOps + QA
- Status: Not Started
- Required action:
  - DevOps and QA signoff after control verification and backup/restore drill completion.
