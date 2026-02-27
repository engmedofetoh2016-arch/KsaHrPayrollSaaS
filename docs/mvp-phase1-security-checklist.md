# MVP Phase 1 Security Checklist (DevOps + QA)

Status key:
- `[ ]` pending
- `[x]` completed

## 1) Secrets Management

- [ ] No production secrets committed in repository.
- [ ] API secrets loaded only from environment/secret store.
- [ ] Database credentials rotated and documented.
- [ ] JWT signing key stored in secure secret source.
- [ ] Least-privilege service credentials verified.

## 2) Data Protection and Storage

- [ ] Database encryption-at-rest enabled and verified.
- [ ] Backup encryption enabled for database snapshots.
- [ ] Access to backups restricted to authorized roles only.
- [ ] Sensitive data handling reviewed for logs and exports.

## 3) Backup and Restore Drill

- [ ] Backup schedule documented (frequency, retention, owner).
- [ ] Restore drill executed in staging within last 30 days.
- [ ] Restore RTO/RPO recorded and accepted.
- [ ] Restore validation includes payroll export artifacts and audit logs.
- [ ] Drill evidence attached (commands, timestamps, results).
- Reference runbook: `docs/backup-restore-drill-runbook.md`
- Reference report template: `docs/backup-restore-drill-report-template.md`

## 4) Runtime and Network Baseline

- [ ] HTTPS enforced in production.
- [ ] CORS allow-list reviewed and restricted.
- [ ] Security headers validated (HSTS, X-Content-Type-Options, etc.).
- [ ] Database not publicly exposed.
- [ ] Monitoring/alerting configured for auth and export failures.

## 5) Compliance Traceability

- [ ] WPS/GOSI export attempts produce audit record for all outcomes.
- [ ] Rejection reasons are machine-readable and test-covered.
- [ ] Audit access endpoint restricted to approved roles.

## Sign-off

- DevOps owner: __________________  Date: __________
- QA owner: ______________________  Date: __________
- Evidence template: `docs/security-signoff-evidence-template.md`
