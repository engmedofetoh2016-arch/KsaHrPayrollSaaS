import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Subscription, interval } from 'rxjs';
import { Employee } from '../../core/models/employee.models';
import {
  ExportJob,
  PayrollApprovalDecision,
  PayrollApprovalFindingSnapshot,
  PayrollPeriod,
  PayrollPreApprovalChecksResult,
  PayrollRunDetails
} from '../../core/models/payroll.models';
import { EmployeesService } from '../../core/services/employees.service';
import { I18nService } from '../../core/services/i18n.service';
import { PayrollService } from '../../core/services/payroll.service';
import { AuthService } from '../../core/services/auth.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-payroll-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './payroll-page.component.html',
  styleUrl: './payroll-page.component.scss'
})
export class PayrollPageComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly payrollService = inject(PayrollService);
  private readonly employeesService = inject(EmployeesService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  readonly i18n = inject(I18nService);

  readonly periods = signal<PayrollPeriod[]>([]);
  readonly employees = signal<Employee[]>([]);
  readonly adjustments = signal<any[]>([]);
  readonly run = signal<PayrollRunDetails | null>(null);
  readonly exports = signal<ExportJob[]>([]);
  readonly approvalDecisions = signal<PayrollApprovalDecision[]>([]);
  readonly activeDecisionSnapshot = signal<{
    decision: PayrollApprovalDecision;
    findings: PayrollApprovalFindingSnapshot[];
  } | null>(null);
  readonly preApprovalChecks = signal<PayrollPreApprovalChecksResult | null>(null);
  readonly activeRunId = signal('');
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly exportBusy = signal(false);
  readonly pollingExports = signal(false);
  readonly loadingChecks = signal(false);
  readonly loadingApprovalDecisions = signal(false);
  readonly loadingReferenceId = signal(false);
  readonly exportsLastUpdatedAt = signal<Date | null>(null);
  readonly message = signal('');
  readonly error = signal('');
  private exportPollSub: Subscription | null = null;

  readonly totalNet = computed(() => (this.run()?.lines ?? []).reduce((sum, line) => sum + line.netAmount, 0));
  readonly criticalChecksCount = computed(
    () => this.preApprovalChecks()?.findings.filter((x) => x.severity === 'Critical').length ?? 0
  );
  readonly warningChecksCount = computed(
    () => this.preApprovalChecks()?.findings.filter((x) => x.severity === 'Warning').length ?? 0
  );
  readonly snapshotCriticalCount = computed(
    () => this.activeDecisionSnapshot()?.findings.filter((x) => (x.severity ?? '').toLowerCase() === 'critical').length ?? 0
  );
  readonly snapshotWarningCount = computed(
    () => this.activeDecisionSnapshot()?.findings.filter((x) => (x.severity ?? '').toLowerCase() === 'warning').length ?? 0
  );
  readonly snapshotNoticeCount = computed(
    () => this.activeDecisionSnapshot()?.findings.filter((x) => (x.severity ?? '').toLowerCase() === 'notice').length ?? 0
  );
  readonly canOverrideApprove = computed(() => this.auth.hasAnyRole(['Owner']));

  readonly periodForm = this.fb.group({
    year: [new Date().getFullYear(), [Validators.required, Validators.min(2000), Validators.max(2100)]],
    month: [new Date().getMonth() + 1, [Validators.required, Validators.min(1), Validators.max(12)]],
    periodStartDate: ['', [Validators.required]],
    periodEndDate: ['', [Validators.required]]
  });

  readonly adjustmentForm = this.fb.group({
    employeeId: ['', [Validators.required]],
    year: [new Date().getFullYear(), [Validators.required, Validators.min(2000), Validators.max(2100)]],
    month: [new Date().getMonth() + 1, [Validators.required, Validators.min(1), Validators.max(12)]],
    type: [1, [Validators.required]],
    amount: [0, [Validators.required, Validators.min(0.01)]],
    notes: ['']
  });

  readonly runForm = this.fb.group({
    payrollPeriodId: ['', [Validators.required]]
  });

  readonly overrideApproveForm = this.fb.group({
    category: ['DataCorrection', [Validators.required]],
    referenceId: ['', [Validators.required, Validators.maxLength(80), Validators.pattern('^OVR-[0-9]{6}-[0-9]{4}$')]],
    reason: ['', [Validators.required, Validators.maxLength(300)]]
  });

  readonly overrideReasonTemplates: ReadonlyArray<{ label: string; value: string }> = [
    { label: 'Data correction', value: 'DataCorrection' },
    { label: 'Timing adjustment', value: 'TimingAdjustment' },
    { label: 'Exceptional payment', value: 'ExceptionalPayment' },
    { label: 'Policy exception', value: 'PolicyException' },
    { label: 'Emergency closure', value: 'EmergencyClosure' },
    { label: 'Other', value: 'Other' }
  ];

  readonly overrideReasonTextTemplates: Record<string, string> = {
    DataCorrection: 'Critical finding acknowledged. Data source corrected and revalidated with HR records.',
    TimingAdjustment: 'Critical finding acknowledged. Month-end timing constraint; documented adjustment approved by owner.',
    ExceptionalPayment: 'Critical finding acknowledged. Exceptional one-time payment authorized by owner with supporting memo.',
    PolicyException: 'Critical finding acknowledged. Approved policy exception with documented justification.',
    EmergencyClosure: 'Critical finding acknowledged. Emergency payroll closure required to meet payment deadline.',
    Other: 'Critical finding acknowledged. Owner approved override with documented business reason.'
  };

  formatUnpaidLeaveFormula(line: PayrollRunDetails['lines'][number]): string {
    const dailyRate = line.baseSalary / 30;
    return `${this.money(line.unpaidLeaveDays)} d x ${this.money(dailyRate)} = ${this.money(line.unpaidLeaveDeduction)}`;
  }

  formatTotalDeductionsFormula(line: PayrollRunDetails['lines'][number]): string {
    return `${this.money(line.manualDeductions)} + ${this.money(line.unpaidLeaveDeduction)} + ${this.money(line.gosiEmployeeContribution)} = ${this.money(line.deductions)}`;
  }

  formatGosiFormula(line: PayrollRunDetails['lines'][number]): string {
    const employerRate = line.gosiWageBase > 0 ? (line.gosiEmployerContribution / line.gosiWageBase) * 100 : 0;
    const employeeRate = line.gosiWageBase > 0 ? (line.gosiEmployeeContribution / line.gosiWageBase) * 100 : 0;
    return `${this.money(line.gosiWageBase)} x ${this.money(employeeRate)}% / ${this.money(employerRate)}%`;
  }

  formatNetFormula(line: PayrollRunDetails['lines'][number]): string {
    const gross = line.baseSalary + line.allowances + line.overtimeAmount;
    return `${this.money(gross)} - ${this.money(line.deductions)} = ${this.money(line.netAmount)}`;
  }

  ngOnInit(): void {
    this.loadBaseData();
    this.generateNextReferenceId();

    const linkedRunId = this.route.snapshot.queryParamMap.get('runId');
    if (linkedRunId && this.looksLikeGuid(linkedRunId)) {
      this.activeRunId.set(linkedRunId);
      this.fetchRun();
      this.message.set(this.i18n.text('Loaded payroll run from governance link.', 'تم تحميل تشغيل الرواتب من رابط الحوكمة.'));
    }
  }

  ngOnDestroy(): void {
    this.stopExportPolling();
  }

  loadBaseData() {
    this.loading.set(true);
    this.error.set('');

    this.payrollService.listPeriods().subscribe({
      next: (periods) => {
        this.periods.set(periods);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load payroll periods.'));
      }
    });

    this.employeesService.list().subscribe({
      next: (employees) => this.employees.set(employees),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load employees.'))
    });
  }

  createPeriod() {
    if (this.periodForm.invalid || this.busy()) {
      this.periodForm.markAllAsTouched();
      return;
    }

    const v = this.periodForm.getRawValue();
    this.busy.set(true);
    this.message.set('');
    this.error.set('');

    this.payrollService
      .createPeriod({
        year: Number(v.year),
        month: Number(v.month),
        periodStartDate: String(v.periodStartDate),
        periodEndDate: String(v.periodEndDate)
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.message.set('Payroll period created.');
          this.loadBaseData();
        },
        error: (err) => {
          this.busy.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to create payroll period.'));
        }
      });
  }

  loadAdjustments() {
    const v = this.adjustmentForm.getRawValue();
    this.payrollService.listAdjustments(Number(v.year), Number(v.month)).subscribe({
      next: (rows) => this.adjustments.set(rows),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load adjustments.'))
    });
  }

  createAdjustment() {
    if (this.adjustmentForm.invalid || this.busy()) {
      this.adjustmentForm.markAllAsTouched();
      return;
    }

    const v = this.adjustmentForm.getRawValue();
    this.busy.set(true);
    this.message.set('');
    this.error.set('');

    this.payrollService
      .createAdjustment({
        employeeId: String(v.employeeId),
        year: Number(v.year),
        month: Number(v.month),
        type: Number(v.type),
        amount: Number(v.amount),
        notes: String(v.notes ?? '')
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.message.set('Adjustment saved.');
          this.loadAdjustments();
        },
        error: (err) => {
          this.busy.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to save adjustment.'));
        }
      });
  }

  calculateRun() {
    if (this.runForm.invalid || this.busy()) {
      this.runForm.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.message.set('');
    this.error.set('');

    this.payrollService.calculateRun(String(this.runForm.getRawValue().payrollPeriodId)).subscribe({
      next: ({ runId }) => {
        this.activeRunId.set(runId);
        this.fetchRun();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to calculate payroll run.'));
      }
    });
  }

  fetchRun() {
    const runId = this.activeRunId();
    if (!runId) return;

    this.payrollService.getRun(runId).subscribe({
      next: (run) => {
        this.run.set(run);
        this.loadPreApprovalChecks(run.id);
        this.loadApprovalDecisions(run.id);
        this.loadExports(true);
        this.busy.set(false);
        this.message.set('Payroll run loaded.');
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load payroll run details.'));
      }
    });
  }

  approveRun() {
    const runId = this.activeRunId();
    if (!runId || this.busy()) return;
    if (this.preApprovalChecks()?.hasBlockingFindings) {
      this.error.set(
        this.i18n.text(
          'Approval blocked by critical payroll findings. Review and fix issues first.',
          'تم منع الاعتماد بسبب ملاحظات حرجة في الرواتب. راجع المشاكل وصححها أولاً.'
        )
      );
      return;
    }

    this.busy.set(true);
    this.payrollService.approveRun(runId).subscribe({
      next: () => {
        this.busy.set(false);
        this.message.set('Payroll run approved.');
        this.fetchRun();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to approve payroll run.'));
      }
    });
  }

  approveRunWithOverride() {
    const runId = this.activeRunId();
    if (!runId || this.busy()) {
      return;
    }

    if (!this.canOverrideApprove()) {
      this.error.set(this.i18n.text('Only Owner can override approval.', 'المالك فقط يمكنه تجاوز الاعتماد.'));
      return;
    }

    if (this.overrideApproveForm.invalid) {
      this.overrideApproveForm.markAllAsTouched();
      this.error.set(
        this.i18n.text(
          'Override reason is required (max 300 chars).',
          'سبب التجاوز مطلوب (حد أقصى 300 حرف).'
        )
      );
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    const raw = this.overrideApproveForm.getRawValue() as { category: string; referenceId: string; reason: string };
    const category = String(raw.category ?? '').trim();
    const referenceId = String(raw.referenceId ?? '').trim();
    const reason = String(raw.reason ?? '').trim();

    this.payrollService.approveRunWithOverride(runId, category, reason, referenceId).subscribe({
      next: () => {
        this.busy.set(false);
        this.message.set(this.i18n.text('Payroll run approved with Owner override.', 'تم اعتماد التشغيل بتجاوز من المالك.'));
        this.fetchRun();
      },
      error: (err) => {
        this.busy.set(false);
        const suggestedReferenceId = String(err?.error?.suggestedReferenceId ?? '').trim();
        if (suggestedReferenceId) {
          this.overrideApproveForm.patchValue({ referenceId: suggestedReferenceId });
          const base = getApiErrorMessage(err, 'Failed to override-approve payroll run.');
          this.error.set(`${base} Suggested reference: ${suggestedReferenceId}`);
          return;
        }

        this.error.set(getApiErrorMessage(err, 'Failed to override-approve payroll run.'));
      }
    });
  }

  applyOverrideReasonTemplate() {
    const category = String(this.overrideApproveForm.getRawValue().category ?? 'Other');
    const template = this.overrideReasonTextTemplates[category] ?? this.overrideReasonTextTemplates['Other'];
    this.overrideApproveForm.patchValue({
      reason: template
    });

    if (!this.overrideApproveForm.getRawValue().referenceId) {
      this.generateNextReferenceId();
    }
  }

  generateNextReferenceId() {
    this.loadingReferenceId.set(true);
    this.payrollService.getNextOverrideReferenceId().subscribe({
      next: (res) => {
        const referenceId = String(res?.referenceId ?? '').trim();
        if (referenceId) {
          this.overrideApproveForm.patchValue({ referenceId });
        }
        if (res?.isSequenceExhausted) {
          this.error.set(this.i18n.text('Override reference sequence reached monthly limit (9999).', 'وصل تسلسل مراجع التجاوز للحد الشهري (9999).'));
        }
      },
      error: () => {},
      complete: () => this.loadingReferenceId.set(false)
    });
  }

  lockRun() {
    const runId = this.activeRunId();
    if (!runId || this.busy()) return;

    this.busy.set(true);
    this.payrollService.lockRun(runId).subscribe({
      next: () => {
        this.busy.set(false);
        this.message.set('Payroll run locked.');
        this.fetchRun();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to lock payroll run.'));
      }
    });
  }

  downloadRegisterCsv() {
    const runId = this.activeRunId();
    if (!runId || this.exportBusy()) return;

    this.exportBusy.set(true);
    this.error.set('');
    this.message.set('Export queued. Generating payroll register...');
    this.payrollService.queueRegisterCsvExport(runId).subscribe({
      next: () => {
        this.exportBusy.set(false);
        this.loadExports(true);
      },
      error: (err) => {
        this.exportBusy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to queue payroll register export.'));
      }
    });
  }

  downloadGosiCsv() {
    const runId = this.activeRunId();
    if (!runId || this.exportBusy()) return;

    this.exportBusy.set(true);
    this.error.set('');
    this.message.set(this.i18n.text('Export queued. Generating GOSI CSV...', 'تمت إضافة التصدير. جاري إنشاء ملف التأمينات...'));
    this.payrollService.queueGosiCsvExport(runId).subscribe({
      next: () => {
        this.exportBusy.set(false);
        this.loadExports(true);
      },
      error: (err) => {
        this.exportBusy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to queue GOSI CSV export.'));
      }
    });
  }

  downloadWpsCsv() {
    const runId = this.activeRunId();
    if (!runId || this.exportBusy()) return;

    this.exportBusy.set(true);
    this.error.set('');
    this.message.set(this.i18n.text('Export queued. Generating WPS CSV...', 'تمت إضافة التصدير. جاري إنشاء ملف WPS...'));
    this.payrollService.queueWpsCsvExport(runId).subscribe({
      next: () => {
        this.exportBusy.set(false);
        this.loadExports(true);
      },
      error: (err) => {
        this.exportBusy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to queue WPS CSV export.'));
      }
    });
  }

  downloadPayslipPdf(employeeId: string, employeeName: string) {
    const runId = this.activeRunId();
    if (!runId || this.exportBusy()) return;

    this.exportBusy.set(true);
    this.error.set('');
    this.message.set(`Export queued. Generating payslip for ${employeeName}...`);
    this.payrollService.queuePayslipPdfExport(runId, employeeId).subscribe({
      next: () => {
        this.exportBusy.set(false);
        this.loadExports(true);
      },
      error: (err) => {
        this.exportBusy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to queue payslip export.'));
      }
    });
  }

  downloadCompletedExport(exportId: string, fileName: string) {
    if (this.exportBusy()) return;
    this.exportBusy.set(true);
    this.payrollService.downloadExport(exportId).subscribe({
      next: (blob) => {
        this.exportBusy.set(false);
        this.downloadBlob(blob, fileName);
      },
      error: (err) => {
        this.exportBusy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to download export file.'));
      }
    });
  }

  loadExports(showLoader = false) {
    const runId = this.activeRunId();
    if (!runId) return;

    if (showLoader) {
      this.exportBusy.set(true);
    }

    this.payrollService.listRunExports(runId).subscribe({
      next: (rows) => {
        if (showLoader) {
          this.exportBusy.set(false);
        }

        this.exports.set(rows);
        this.exportsLastUpdatedAt.set(new Date());
        if (rows.some((x) => x.status === 1 || x.status === 2)) {
          this.startExportPolling();
        } else {
          this.stopExportPolling();
        }
      },
      error: (err) => {
        if (showLoader) {
          this.exportBusy.set(false);
        }
        this.stopExportPolling();
        this.error.set(getApiErrorMessage(err, 'Failed to load export history.'));
      }
    });
  }

  private downloadBlob(blob: Blob, fileName: string) {
    const url = window.URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    window.URL.revokeObjectURL(url);
  }

  exportStatusLabel(status: number) {
    switch (status) {
      case 1: return this.i18n.text('Pending', 'قيد الانتظار');
      case 2: return this.i18n.text('Processing', 'قيد المعالجة');
      case 3: return this.i18n.text('Completed', 'مكتمل');
      case 4: return this.i18n.text('Failed', 'فشل');
      default: return this.i18n.text('Unknown', 'غير معروف');
    }
  }

  exportStatusClass(status: number) {
    switch (status) {
      case 1: return 'badge pending';
      case 2: return 'badge processing';
      case 3: return 'badge completed';
      case 4: return 'badge failed';
      default: return 'badge';
    }
  }

  exportLastUpdatedLabel() {
    const updatedAt = this.exportsLastUpdatedAt();
    if (!updatedAt) {
      return this.i18n.text('Not updated yet', 'لم يتم التحديث بعد');
    }

    const seconds = Math.max(0, Math.floor((Date.now() - updatedAt.getTime()) / 1000));
    return this.i18n.text(`Last updated ${seconds}s ago`, `آخر تحديث قبل ${seconds} ث`);
  }

  checkSeverityClass(severity: string) {
    const normalized = (severity ?? '').toLowerCase();
    if (normalized === 'critical') {
      return 'badge failed';
    }
    if (normalized === 'warning') {
      return 'badge pending';
    }
    return 'badge processing';
  }

  openDecisionSnapshot(decision: PayrollApprovalDecision) {
    const raw = decision.findingsSnapshotJson;
    if (!raw) {
      this.activeDecisionSnapshot.set({
        decision,
        findings: []
      });
      return;
    }

    try {
      const parsed = JSON.parse(raw) as any[];
      const findings: PayrollApprovalFindingSnapshot[] = Array.isArray(parsed)
        ? parsed.map((item) => {
            const employeeIdValue = item.employeeId ?? item.EmployeeId;
            const employeeNameValue = item.employeeName ?? item.EmployeeName;
            const metricNameValue = item.metricName ?? item.MetricName;
            const metricValueRaw = item.metricValue ?? item.MetricValue;

            return {
              code: String(item.code ?? item.Code ?? ''),
              severity: String(item.severity ?? item.Severity ?? 'Notice'),
              employeeId: employeeIdValue ? String(employeeIdValue) : undefined,
              employeeName: employeeNameValue ? String(employeeNameValue) : undefined,
              message: String(item.message ?? item.Message ?? ''),
              metricName: metricNameValue ? String(metricNameValue) : undefined,
              metricValue: typeof metricValueRaw === 'number' ? Number(metricValueRaw) : undefined
            };
          })
        : [];

      this.activeDecisionSnapshot.set({
        decision,
        findings
      });
    } catch {
      this.activeDecisionSnapshot.set({
        decision,
        findings: []
      });
    }
  }

  closeDecisionSnapshot() {
    this.activeDecisionSnapshot.set(null);
  }

  private loadPreApprovalChecks(runId: string) {
    if (!runId) {
      this.preApprovalChecks.set(null);
      return;
    }

    this.loadingChecks.set(true);
    this.payrollService.getPreApprovalChecks(runId).subscribe({
      next: (result) => this.preApprovalChecks.set(result),
      error: (err) => {
        this.preApprovalChecks.set(null);
        this.error.set(getApiErrorMessage(err, 'Failed to load payroll pre-approval checks.'));
      },
      complete: () => this.loadingChecks.set(false)
    });
  }

  private loadApprovalDecisions(runId: string) {
    if (!runId) {
      this.approvalDecisions.set([]);
      return;
    }

    this.loadingApprovalDecisions.set(true);
    this.payrollService.getApprovalDecisions(runId).subscribe({
      next: (response) => this.approvalDecisions.set(Array.isArray(response.items) ? response.items : []),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load payroll approval decisions.')),
      complete: () => this.loadingApprovalDecisions.set(false)
    });
  }

  private startExportPolling() {
    if (this.exportPollSub) {
      return;
    }

    this.pollingExports.set(true);
    this.exportPollSub = interval(4000).subscribe(() => this.loadExports(false));
  }

  private stopExportPolling() {
    this.exportPollSub?.unsubscribe();
    this.exportPollSub = null;
    this.pollingExports.set(false);
  }

  private money(value: number): string {
    return Number(value).toFixed(2);
  }

  private looksLikeGuid(value: string): boolean {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
  }
}
