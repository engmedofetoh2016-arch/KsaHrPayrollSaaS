# MVP Phase 1 Export Spec Baseline

This document freezes the MVP compliance export specs for the initial launch baseline.

## Scope

- WPS CSV export
- GOSI CSV export

## Version Baseline

- `WPS_CSV` version: `v1`
- `GOSI_CSV` version: `v1`
- Trace format version in metadata: `2026.02.v1`

## Contract

1. Every export request must include:
- `exportSpec.code`
- `exportSpec.version`
- `traceVersion`

2. Validation failures must return:
- `error`
- `details` (human-readable summary)
- `rejectionReasons[]` with:
  - `code` (machine-readable rejection reason)
  - `message` (human-readable reason)

3. Every WPS/GOSI attempt must write an audit record with:
- action method (`PAYROLL_EXPORT_REQUEST`)
- endpoint path
- status (`Queued`, `Blocked`, or `Rejected`)
- spec + version
- rejection reason code list when blocked

## Change Control

Any change to `WPS_CSV` or `GOSI_CSV` validation behavior must:

1. bump the spec version
2. update automated tests
3. update this file and release notes
