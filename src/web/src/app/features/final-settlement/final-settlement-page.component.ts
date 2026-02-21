import { CommonModule } from '@angular/common';
import { HttpResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, interval } from 'rxjs';
import {
  Employee,
  FinalSettlementExportJob,
  FinalSettlementEstimateResult
} from '../../core/models/employee.models';
import { EmployeesService } from '../../core/services/employees.service';
import { I18nService } from '../../core/services/i18n.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-final-settlement-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './final-settlement-page.component.html',
  styleUrl: './final-settlement-page.component.scss'
})
export class FinalSettlementPageComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly employeesService = inject(EmployeesService);
  readonly i18n = inject(I18nService);

  readonly employees = signal<Employee[]>([]);
  readonly exports = signal<FinalSettlementExportJob[]>([]);
  readonly result = signal<FinalSettlementEstimateResult | null>(null);
  readonly loading = signal(false);
  readonly loadingExports = signal(false);
  readonly processing = signal(false);
  readonly queueing = signal(false);
  readonly bulkQueueing = signal(false);
  readonly exportingCsv = signal(false);
  readonly exportingPdf = signal(false);
  readonly downloadingZip = signal(false);
  readonly retrying = signal(false);
  readonly retryingAllFailed = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly bulkQueueSummary = signal('');
  readonly statusFilter = signal(-1);
  readonly exportsScope = signal<'employee' | 'all'>('employee');

  private pollSub: Subscription | null = null;

  readonly form = this.fb.group({
    employeeId: ['', [Validators.required]],
    terminationDate: [new Date().toISOString().slice(0, 10), [Validators.required]],
    year: [new Date().getFullYear(), [Validators.required, Validators.min(2000), Validators.max(2100)]],
    month: [new Date().getMonth() + 1, [Validators.required, Validators.min(1), Validators.max(12)]],
    additionalManualDeduction: [0, [Validators.required, Validators.min(0)]],
    notes: ['']
  });

  readonly filteredExports = computed(() => {
    const status = this.statusFilter();
    if (status < 0) {
      return this.exports();
    }
    return this.exports().filter((x) => x.status === status);
  });

  ngOnInit(): void {
    this.loadEmployees();
  }

  ngOnDestroy(): void {
    this.stopPolling();
  }

  loadEmployees() {
    this.loading.set(true);
    this.error.set('');

    this.employeesService.list().subscribe({
      next: (rows) => {
        this.loading.set(false);
        this.employees.set(rows);
        if (!this.form.getRawValue().employeeId && rows.length > 0) {
          this.form.patchValue({ employeeId: rows[0].id });
          this.refreshExports();
        }
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load employees.'));
      }
    });
  }

  calculate() {
    if (this.form.invalid || this.processing()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const employeeId = String(value.employeeId ?? '');

    this.processing.set(true);
    this.error.set('');
    this.message.set('');

    this.employeesService
      .estimateFinalSettlement(employeeId, {
        terminationDate: String(value.terminationDate ?? ''),
        year: Number(value.year),
        month: Number(value.month),
        additionalManualDeduction: Number(value.additionalManualDeduction ?? 0),
        notes: value.notes ?? ''
      })
      .subscribe({
        next: (result) => {
          this.processing.set(false);
          this.result.set(result);
        },
        error: (err) => {
          this.processing.set(false);
          this.result.set(null);
          this.error.set(getApiErrorMessage(err, 'Failed to calculate final settlement.'));
        }
      });
  }

  exportCsv() {
    if (this.form.invalid || this.exportingCsv()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const employeeId = String(value.employeeId ?? '');

    this.exportingCsv.set(true);
    this.error.set('');
    this.message.set('');

    this.employeesService
      .exportFinalSettlementCsv(employeeId, {
        terminationDate: String(value.terminationDate ?? ''),
        year: Number(value.year),
        month: Number(value.month),
        additionalManualDeduction: Number(value.additionalManualDeduction ?? 0),
        notes: value.notes ?? ''
      })
      .subscribe({
        next: (response) => {
          this.exportingCsv.set(false);
          this.download(response, 'final-settlement.csv');
          this.message.set(this.i18n.text('Final settlement CSV downloaded.', 'تم تنزيل CSV للتسوية النهائية.'));
        },
        error: (err) => {
          this.exportingCsv.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to export CSV.'));
        }
      });
  }

  exportPdf() {
    if (this.form.invalid || this.exportingPdf()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const employeeId = String(value.employeeId ?? '');

    this.exportingPdf.set(true);
    this.error.set('');
    this.message.set('');

    this.employeesService
      .exportFinalSettlementPdf(employeeId, {
        terminationDate: String(value.terminationDate ?? ''),
        year: Number(value.year),
        month: Number(value.month),
        additionalManualDeduction: Number(value.additionalManualDeduction ?? 0),
        notes: value.notes ?? ''
      })
      .subscribe({
        next: (response) => {
          this.exportingPdf.set(false);
          this.download(response, 'final-settlement.pdf');
          this.message.set(this.i18n.text('Final settlement PDF downloaded.', 'تم تنزيل PDF للتسوية النهائية.'));
        },
        error: (err) => {
          this.exportingPdf.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to export PDF.'));
        }
      });
  }

  queuePdf() {
    if (this.form.invalid || this.queueing()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const employeeId = String(value.employeeId ?? '');

    this.queueing.set(true);
    this.error.set('');
    this.message.set('');

    this.employeesService
      .queueFinalSettlementPdfExport(employeeId, {
        terminationDate: String(value.terminationDate ?? ''),
        year: Number(value.year),
        month: Number(value.month),
        additionalManualDeduction: Number(value.additionalManualDeduction ?? 0),
        notes: value.notes ?? ''
      })
      .subscribe({
        next: (job) => {
          this.queueing.set(false);
          this.message.set(this.i18n.text(`PDF queued (Job: ${job.id}).`, `تمت جدولة PDF (المهمة: ${job.id}).`));
          this.refreshExports();
        },
        error: (err) => {
          this.queueing.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to queue PDF export.'));
        }
      });
  }

  queuePdfForAllEmployees() {
    if (this.bulkQueueing() || this.employees().length === 0) {
      return;
    }

    const value = this.form.getRawValue();
    const terminationDate = String(value.terminationDate ?? '');
    const year = Number(value.year);
    const month = Number(value.month);
    const additionalManualDeduction = Number(value.additionalManualDeduction ?? 0);
    const notes = value.notes ?? '';
    const ids = this.employees().map((x) => x.id);

    this.bulkQueueing.set(true);
    this.error.set('');
    this.message.set('');
    this.bulkQueueSummary.set('');

    let success = 0;
    let failed = 0;
    let index = 0;

    const runNext = () => {
      if (index >= ids.length) {
        this.bulkQueueing.set(false);
        this.bulkQueueSummary.set(
          this.i18n.text(
            `Bulk queue completed. Success: ${success}, Failed: ${failed}`,
            `اكتملت الجدولة الجماعية. نجح: ${success}، فشل: ${failed}`
          )
        );
        this.refreshExports();
        return;
      }

      const employeeId = ids[index];
      index += 1;

      this.employeesService
        .queueFinalSettlementPdfExport(employeeId, {
          terminationDate,
          year,
          month,
          additionalManualDeduction,
          notes
        })
        .subscribe({
          next: () => {
            success += 1;
            runNext();
          },
          error: () => {
            failed += 1;
            runNext();
          }
        });
    };

    runNext();
  }

  refreshExports(showLoader = true) {
    const employeeId = String(this.form.getRawValue().employeeId ?? '');
    if (this.exportsScope() === 'employee' && !employeeId) {
      this.exports.set([]);
      this.stopPolling();
      return;
    }

    if (showLoader) {
      this.loadingExports.set(true);
    }

    const statusFilterValue = this.statusFilter();
    const request = this.exportsScope() === 'all'
      ? this.employeesService.listFinalSettlementExportsGlobal(undefined, statusFilterValue > 0 ? statusFilterValue : undefined, 200)
      : this.employeesService.listFinalSettlementExports(employeeId);

    request.subscribe({
      next: (rows) => {
        this.loadingExports.set(false);
        this.exports.set(rows);
        if (rows.some((x) => x.status === 1 || x.status === 2)) {
          this.startPolling();
        } else {
          this.stopPolling();
        }
      },
      error: (err) => {
        this.loadingExports.set(false);
        this.stopPolling();
        this.error.set(getApiErrorMessage(err, 'Failed to load exports.'));
      }
    });
  }

  downloadExport(exportId: string) {
    this.error.set('');
    this.message.set('');
    this.employeesService.downloadFinalSettlementExport(exportId).subscribe({
      next: (response) => this.download(response, 'final-settlement.pdf'),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Export is not ready yet.'))
    });
  }

  retryExport(exportId: string) {
    if (this.retrying()) {
      return;
    }

    this.retrying.set(true);
    this.error.set('');
    this.message.set('');

    this.employeesService.retryFinalSettlementExport(exportId).subscribe({
      next: () => {
        this.retrying.set(false);
        this.message.set(this.i18n.text('Export was queued for retry.', 'تمت جدولة إعادة المحاولة للتصدير.'));
        this.refreshExports();
      },
      error: (err) => {
        this.retrying.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to retry export.'));
      }
    });
  }

  retryAllFailed() {
    if (this.retryingAllFailed()) {
      return;
    }

    const failed = this.filteredExports().filter((x) => x.status === 4);
    if (failed.length === 0) {
      this.message.set(this.i18n.text('No failed exports to retry.', 'لا يوجد تصديرات فاشلة لإعادة المحاولة.'));
      return;
    }

    this.retryingAllFailed.set(true);
    this.error.set('');
    this.message.set('');

    let success = 0;
    let failedCount = 0;
    let index = 0;

    const runNext = () => {
      if (index >= failed.length) {
        this.retryingAllFailed.set(false);
        this.message.set(
          this.i18n.text(
            `Retry finished. Success: ${success}, Failed: ${failedCount}`,
            `اكتملت إعادة المحاولة. نجح: ${success}، فشل: ${failedCount}`
          )
        );
        this.refreshExports();
        return;
      }

      const job = failed[index];
      index += 1;

      this.employeesService.retryFinalSettlementExport(job.id).subscribe({
        next: () => {
          success += 1;
          runNext();
        },
        error: () => {
          failedCount += 1;
          runNext();
        }
      });
    };

    runNext();
  }

  downloadCompletedZip() {
    const employeeId = String(this.form.getRawValue().employeeId ?? '');
    if (this.downloadingZip()) {
      return;
    }

    if (this.exportsScope() === 'employee' && !employeeId) {
      return;
    }

    this.downloadingZip.set(true);
    this.error.set('');
    this.message.set('');

    const request = this.exportsScope() === 'all'
      ? this.employeesService.downloadFinalSettlementExportZipGlobal()
      : this.employeesService.downloadFinalSettlementExportZip(employeeId);

    request.subscribe({
      next: (response) => {
        this.downloadingZip.set(false);
        this.download(response, 'final-settlement-exports.zip');
      },
      error: (err) => {
        this.downloadingZip.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to download ZIP.'));
      }
    });
  }

  setExportScope(scope: 'employee' | 'all') {
    this.exportsScope.set(scope);
    this.refreshExports();
  }

  setStatusFilter(rawValue: string) {
    const parsed = Number(rawValue);
    this.statusFilter.set(Number.isNaN(parsed) ? -1 : parsed);
  }

  statusLabel(status: number) {
    switch (status) {
      case 1: return this.i18n.text('Pending', 'قيد الانتظار');
      case 2: return this.i18n.text('Processing', 'قيد المعالجة');
      case 3: return this.i18n.text('Completed', 'مكتمل');
      case 4: return this.i18n.text('Failed', 'فشل');
      default: return String(status);
    }
  }

  statusClass(status: number) {
    switch (status) {
      case 1: return 'badge pending';
      case 2: return 'badge processing';
      case 3: return 'badge completed';
      case 4: return 'badge failed';
      default: return 'badge';
    }
  }

  private startPolling() {
    if (this.pollSub) {
      return;
    }
    this.pollSub = interval(4000).subscribe(() => this.refreshExports(false));
  }

  private stopPolling() {
    this.pollSub?.unsubscribe();
    this.pollSub = null;
  }

  private download(response: HttpResponse<Blob>, fallbackFileName: string) {
    const blob = response.body;
    if (!blob) {
      this.error.set(this.i18n.text('Export returned empty response.', 'التصدير أعاد استجابة فارغة.'));
      return;
    }

    const contentDisposition = response.headers.get('content-disposition') ?? '';
    const fileNameMatch = contentDisposition.match(/filename\*?=(?:UTF-8''|\"?)([^\";]+)/i);
    const fileName = fileNameMatch ? decodeURIComponent(fileNameMatch[1].replace(/\"/g, '')) : fallbackFileName;

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
