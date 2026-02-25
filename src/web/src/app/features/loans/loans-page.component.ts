import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Employee } from '../../core/models/employee.models';
import { EmployeeLoan, EmployeeLoanInstallment } from '../../core/models/loan.models';
import { EmployeesService } from '../../core/services/employees.service';
import { I18nService } from '../../core/services/i18n.service';
import { LoanService } from '../../core/services/loan.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-loans-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './loans-page.component.html',
  styleUrl: './loans-page.component.scss'
})
export class LoansPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly employeesService = inject(EmployeesService);
  private readonly loanService = inject(LoanService);
  readonly i18n = inject(I18nService);

  readonly employees = signal<Employee[]>([]);
  readonly loans = signal<EmployeeLoan[]>([]);
  readonly installments = signal<EmployeeLoanInstallment[]>([]);
  readonly activeLoanId = signal('');
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  readonly form = this.fb.group({
    employeeId: ['', [Validators.required]],
    loanType: ['Advance', [Validators.required, Validators.maxLength(80)]],
    principalAmount: [1000, [Validators.required, Validators.min(1)]],
    installmentAmount: [250, [Validators.required, Validators.min(1)]],
    startYear: [new Date().getFullYear(), [Validators.required, Validators.min(2000), Validators.max(2100)]],
    startMonth: [new Date().getMonth() + 1, [Validators.required, Validators.min(1), Validators.max(12)]],
    totalInstallments: [4, [Validators.required, Validators.min(1), Validators.max(120)]],
    notes: ['']
  });

  constructor() {
    this.loadBaseData();
  }

  loadBaseData() {
    this.loading.set(true);
    this.error.set('');
    this.employeesService.list().subscribe({
      next: (rows) => {
        this.employees.set(rows);
        if (!this.form.getRawValue().employeeId && rows.length > 0) {
          this.form.patchValue({ employeeId: rows[0].id });
        }
      },
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load employees.'))
    });

    this.loanService.list().subscribe({
      next: (rows) => {
        this.loans.set(rows);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load loans.'));
      }
    });
  }

  createLoan() {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    const v = this.form.getRawValue();

    this.loanService.create({
      employeeId: String(v.employeeId),
      loanType: String(v.loanType),
      principalAmount: Number(v.principalAmount),
      installmentAmount: Number(v.installmentAmount),
      startYear: Number(v.startYear),
      startMonth: Number(v.startMonth),
      totalInstallments: Number(v.totalInstallments),
      notes: String(v.notes ?? '')
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.message.set('Loan created.');
        this.loadBaseData();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to create loan.'));
      }
    });
  }

  approveLoan(loanId: string) {
    this.saving.set(true);
    this.error.set('');
    this.loanService.approve(loanId).subscribe({
      next: () => {
        this.saving.set(false);
        this.loadBaseData();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to approve loan.'));
      }
    });
  }

  cancelLoan(loanId: string) {
    this.saving.set(true);
    this.error.set('');
    this.loanService.cancel(loanId).subscribe({
      next: () => {
        this.saving.set(false);
        this.loadBaseData();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to cancel loan.'));
      }
    });
  }

  viewInstallments(loanId: string) {
    this.activeLoanId.set(loanId);
    this.installments.set([]);
    this.loanService.listInstallments(loanId).subscribe({
      next: (rows) => this.installments.set(rows),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load installments.'))
    });
  }
}
