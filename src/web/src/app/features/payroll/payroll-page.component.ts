import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, interval } from 'rxjs';
import { Employee } from '../../core/models/employee.models';
import { ExportJob, PayrollPeriod, PayrollRunDetails } from '../../core/models/payroll.models';
import { EmployeesService } from '../../core/services/employees.service';
import { I18nService } from '../../core/services/i18n.service';
import { PayrollService } from '../../core/services/payroll.service';
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
  readonly i18n = inject(I18nService);

  readonly periods = signal<PayrollPeriod[]>([]);
  readonly employees = signal<Employee[]>([]);
  readonly adjustments = signal<any[]>([]);
  readonly run = signal<PayrollRunDetails | null>(null);
  readonly exports = signal<ExportJob[]>([]);
  readonly activeRunId = signal('');
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly exportBusy = signal(false);
  readonly pollingExports = signal(false);
  readonly exportsLastUpdatedAt = signal<Date | null>(null);
  readonly message = signal('');
  readonly error = signal('');
  private exportPollSub: Subscription | null = null;

  readonly totalNet = computed(() => (this.run()?.lines ?? []).reduce((sum, line) => sum + line.netAmount, 0));

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
}
