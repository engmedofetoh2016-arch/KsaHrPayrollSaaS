import { CommonModule } from '@angular/common';
import { HttpResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, interval } from 'rxjs';
import {
  Employee,
  EosEstimateResult,
  FinalSettlementExportJob,
  FinalSettlementEstimateResult
} from '../../core/models/employee.models';
import { EmployeesService } from '../../core/services/employees.service';
import { I18nService } from '../../core/services/i18n.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-employees-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './employees-page.component.html',
  styleUrl: './employees-page.component.scss'
})
export class EmployeesPageComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly employeesService = inject(EmployeesService);
  readonly i18n = inject(I18nService);

  readonly employees = signal<Employee[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly estimatingEos = signal(false);
  readonly estimatingFinalSettlement = signal(false);
  readonly exportingFinalSettlement = signal(false);
  readonly exportingFinalSettlementPdf = signal(false);
  readonly queueingFinalSettlementPdf = signal(false);
  readonly loadingFinalSettlementExports = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly eosResult = signal<EosEstimateResult | null>(null);
  readonly finalSettlementResult = signal<FinalSettlementEstimateResult | null>(null);
  readonly finalSettlementExports = signal<FinalSettlementExportJob[]>([]);
  private finalSettlementPollSub: Subscription | null = null;

  readonly form = this.fb.group({
    startDate: [new Date().toISOString().slice(0, 10), [Validators.required]],
    firstName: ['', [Validators.required, Validators.minLength(2)]],
    lastName: ['', [Validators.required, Validators.minLength(2)]],
    email: ['', [Validators.required, Validators.email]],
    jobTitle: ['', [Validators.required, Validators.minLength(2)]],
    baseSalary: [0, [Validators.required, Validators.min(1)]],
    isSaudiNational: [false],
    isGosiEligible: [false],
    gosiBasicWage: [0, [Validators.required, Validators.min(0)]],
    gosiHousingAllowance: [0, [Validators.required, Validators.min(0)]],
    employeeNumber: [''],
    bankName: [''],
    bankIban: [''],
    iqamaNumber: [''],
    iqamaExpiryDate: [''],
    workPermitExpiryDate: ['']
  });

  readonly eosForm = this.fb.group({
    employeeId: ['', [Validators.required]],
    terminationDate: [new Date().toISOString().slice(0, 10), [Validators.required]]
  });

  readonly finalSettlementForm = this.fb.group({
    employeeId: ['', [Validators.required]],
    terminationDate: [new Date().toISOString().slice(0, 10), [Validators.required]],
    year: [new Date().getFullYear(), [Validators.required, Validators.min(2000), Validators.max(2100)]],
    month: [new Date().getMonth() + 1, [Validators.required, Validators.min(1), Validators.max(12)]],
    additionalManualDeduction: [0, [Validators.required, Validators.min(0)]],
    notes: ['']
  });

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.stopFinalSettlementExportPolling();
  }

  load() {
    this.loading.set(true);
    this.error.set('');

    this.employeesService.list().subscribe({
      next: (employees) => {
        this.employees.set(employees);
        if (!this.eosForm.getRawValue().employeeId && employees.length > 0) {
          this.eosForm.patchValue({ employeeId: employees[0].id });
        }
        if (!this.finalSettlementForm.getRawValue().employeeId && employees.length > 0) {
          this.finalSettlementForm.patchValue({ employeeId: employees[0].id });
        }
        this.refreshFinalSettlementExports();
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load employees.'));
      }
    });
  }

  create() {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.message.set('');
    this.error.set('');

    const value = this.form.getRawValue();
    this.employeesService
      .create({
        startDate: value.startDate ?? new Date().toISOString().slice(0, 10),
        firstName: value.firstName ?? '',
        lastName: value.lastName ?? '',
        email: value.email ?? '',
        jobTitle: value.jobTitle ?? '',
        baseSalary: Number(value.baseSalary ?? 0),
        isSaudiNational: Boolean(value.isSaudiNational),
        isGosiEligible: Boolean(value.isGosiEligible),
        gosiBasicWage: Number(value.gosiBasicWage ?? 0),
        gosiHousingAllowance: Number(value.gosiHousingAllowance ?? 0),
        employeeNumber: value.employeeNumber ?? '',
        bankName: value.bankName ?? '',
        bankIban: value.bankIban ?? '',
        iqamaNumber: value.iqamaNumber ?? '',
        iqamaExpiryDate: value.iqamaExpiryDate || null,
        workPermitExpiryDate: value.workPermitExpiryDate || null
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.message.set('Employee created.');
          this.form.reset({
            startDate: new Date().toISOString().slice(0, 10),
            firstName: '',
            lastName: '',
            email: '',
            jobTitle: '',
            baseSalary: 0,
            isSaudiNational: false,
            isGosiEligible: false,
            gosiBasicWage: 0,
            gosiHousingAllowance: 0,
            employeeNumber: '',
            bankName: '',
            bankIban: '',
            iqamaNumber: '',
            iqamaExpiryDate: '',
            workPermitExpiryDate: ''
          });
          this.load();
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to create employee.'));
        }
      });
  }

  estimateEos() {
    if (this.eosForm.invalid || this.estimatingEos()) {
      this.eosForm.markAllAsTouched();
      return;
    }

    const value = this.eosForm.getRawValue();
    const employeeId = String(value.employeeId ?? '');
    const terminationDate = String(value.terminationDate ?? '');

    this.estimatingEos.set(true);
    this.error.set('');
    this.message.set('');

    this.employeesService.estimateEos(employeeId, terminationDate).subscribe({
      next: (result) => {
        this.estimatingEos.set(false);
        this.eosResult.set(result);
      },
      error: (err) => {
        this.estimatingEos.set(false);
        this.eosResult.set(null);
        this.error.set(getApiErrorMessage(err, 'Failed to estimate EOS.'));
      }
    });
  }

  estimateFinalSettlement() {
    if (this.finalSettlementForm.invalid || this.estimatingFinalSettlement()) {
      this.finalSettlementForm.markAllAsTouched();
      return;
    }

    const value = this.finalSettlementForm.getRawValue();
    const employeeId = String(value.employeeId ?? '');

    this.estimatingFinalSettlement.set(true);
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
          this.estimatingFinalSettlement.set(false);
          this.finalSettlementResult.set(result);
        },
        error: (err) => {
          this.estimatingFinalSettlement.set(false);
          this.finalSettlementResult.set(null);
          this.error.set(getApiErrorMessage(err, 'Failed to estimate final settlement.'));
        }
      });
  }

  exportFinalSettlementCsv() {
    if (this.finalSettlementForm.invalid || this.exportingFinalSettlement()) {
      this.finalSettlementForm.markAllAsTouched();
      return;
    }

    const value = this.finalSettlementForm.getRawValue();
    const employeeId = String(value.employeeId ?? '');

    this.exportingFinalSettlement.set(true);
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
          this.exportingFinalSettlement.set(false);
          this.downloadFileFromResponse(response, 'final-settlement.csv');
          this.message.set('Final settlement CSV downloaded.');
        },
        error: (err) => {
          this.exportingFinalSettlement.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to export final settlement CSV.'));
        }
      });
  }

  exportFinalSettlementPdf() {
    if (this.finalSettlementForm.invalid || this.exportingFinalSettlementPdf()) {
      this.finalSettlementForm.markAllAsTouched();
      return;
    }

    const value = this.finalSettlementForm.getRawValue();
    const employeeId = String(value.employeeId ?? '');

    this.exportingFinalSettlementPdf.set(true);
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
          this.exportingFinalSettlementPdf.set(false);
          this.downloadFileFromResponse(response, 'final-settlement.pdf');
          this.message.set('Final settlement PDF downloaded.');
        },
        error: (err) => {
          this.exportingFinalSettlementPdf.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to export final settlement PDF.'));
        }
      });
  }

  queueFinalSettlementPdfExport() {
    if (this.finalSettlementForm.invalid || this.queueingFinalSettlementPdf()) {
      this.finalSettlementForm.markAllAsTouched();
      return;
    }

    const value = this.finalSettlementForm.getRawValue();
    const employeeId = String(value.employeeId ?? '');

    this.queueingFinalSettlementPdf.set(true);
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
          this.queueingFinalSettlementPdf.set(false);
          this.message.set(`Final settlement PDF queued (Job: ${job.id}).`);
          this.refreshFinalSettlementExports();
        },
        error: (err) => {
          this.queueingFinalSettlementPdf.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to queue final settlement PDF.'));
        }
      });
  }

  refreshFinalSettlementExports(showLoader = true) {
    const employeeId = String(this.finalSettlementForm.getRawValue().employeeId ?? '');
    if (!employeeId) {
      this.finalSettlementExports.set([]);
      this.stopFinalSettlementExportPolling();
      return;
    }

    if (showLoader) {
      this.loadingFinalSettlementExports.set(true);
    }

    this.employeesService.listFinalSettlementExports(employeeId).subscribe({
      next: (rows) => {
        this.loadingFinalSettlementExports.set(false);
        this.finalSettlementExports.set(rows);
        if (rows.some((x) => x.status === 1 || x.status === 2)) {
          this.startFinalSettlementExportPolling();
        } else {
          this.stopFinalSettlementExportPolling();
        }
      },
      error: (err) => {
        this.loadingFinalSettlementExports.set(false);
        this.stopFinalSettlementExportPolling();
        this.error.set(getApiErrorMessage(err, 'Failed to load final settlement exports.'));
      }
    });
  }

  downloadQueuedFinalSettlementExport(exportId: string) {
    this.error.set('');
    this.message.set('');

    this.employeesService.downloadFinalSettlementExport(exportId).subscribe({
      next: (response) => {
        this.downloadFileFromResponse(response, 'final-settlement.pdf');
      },
      error: (err) => {
        this.error.set(getApiErrorMessage(err, 'Export is not ready yet.'));
      }
    });
  }

  exportStatusLabel(status: number) {
    switch (status) {
      case 1: return this.i18n.text('Pending', 'قيد الانتظار');
      case 2: return this.i18n.text('Processing', 'قيد المعالجة');
      case 3: return this.i18n.text('Completed', 'مكتمل');
      case 4: return this.i18n.text('Failed', 'فشل');
      default: return String(status);
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

  private startFinalSettlementExportPolling() {
    if (this.finalSettlementPollSub) {
      return;
    }

    this.finalSettlementPollSub = interval(4000).subscribe(() => {
      this.refreshFinalSettlementExports(false);
    });
  }

  private stopFinalSettlementExportPolling() {
    this.finalSettlementPollSub?.unsubscribe();
    this.finalSettlementPollSub = null;
  }

  private downloadFileFromResponse(response: HttpResponse<Blob>, fallbackFileName: string) {
    const blob = response.body;
    if (!blob) {
      this.error.set('Export returned empty response.');
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
