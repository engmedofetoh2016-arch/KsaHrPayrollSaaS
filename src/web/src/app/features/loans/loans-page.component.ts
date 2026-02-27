import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
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
  readonly lifecycleCheckMessage = signal('');
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly activeLoan = computed(() => this.loans().find((x) => x.id === this.activeLoanId()) ?? null);

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

  readonly lifecycleForm = this.fb.group({
    startYear: [new Date().getFullYear(), [Validators.required, Validators.min(2000), Validators.max(2100)]],
    startMonth: [new Date().getMonth() + 1, [Validators.required, Validators.min(1), Validators.max(12)]],
    settleAmount: [null as number | null, [Validators.min(0.01)]],
    reason: ['']
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
    const loan = this.loans().find((x) => x.id === loanId);
    if (loan) {
      this.lifecycleForm.patchValue({
        startYear: loan.startYear,
        startMonth: loan.startMonth
      });
    }
    this.lifecycleCheckMessage.set('');
    this.installments.set([]);
    this.loanService.listInstallments(loanId).subscribe({
      next: (rows) => this.installments.set(rows),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load installments.'))
    });
  }

  checkLifecycleLock() {
    const loanId = this.activeLoanId();
    if (!loanId || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.lifecycleCheckMessage.set('');
    this.loanService.lifecycleCheck(loanId).subscribe({
      next: (result) => {
        this.saving.set(false);
        if (result.blockedPeriods.length === 0) {
          this.lifecycleCheckMessage.set('No payroll lock blockers for pending installments.');
          return;
        }

        this.lifecycleCheckMessage.set(`Locked periods: ${result.blockedPeriods.join(', ')}`);
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to check payroll lock status.'));
      }
    });
  }

  rescheduleLoan() {
    const loanId = this.activeLoanId();
    if (!loanId || this.lifecycleForm.invalid || this.saving()) {
      this.lifecycleForm.markAllAsTouched();
      return;
    }

    const v = this.lifecycleForm.getRawValue();
    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    this.loanService.reschedule(loanId, {
      startYear: Number(v.startYear),
      startMonth: Number(v.startMonth),
      reason: String(v.reason ?? '')
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.message.set('Loan installments rescheduled.');
        this.viewInstallments(loanId);
        this.loadBaseData();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to reschedule installments.'));
      }
    });
  }

  skipNextInstallment() {
    const loanId = this.activeLoanId();
    if (!loanId || this.saving()) {
      return;
    }

    const v = this.lifecycleForm.getRawValue();
    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    this.loanService.skipNext(loanId, {
      reason: String(v.reason ?? '')
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.message.set('Next installment skipped and moved to end of schedule.');
        this.viewInstallments(loanId);
        this.loadBaseData();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to skip installment.'));
      }
    });
  }

  settleEarlyLoan() {
    const loanId = this.activeLoanId();
    if (!loanId || this.saving()) {
      return;
    }

    const v = this.lifecycleForm.getRawValue();
    const amount = v.settleAmount == null ? null : Number(v.settleAmount);
    if (amount != null && amount <= 0) {
      this.error.set('Settlement amount must be greater than zero.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    this.loanService.settleEarly(loanId, {
      amount,
      reason: String(v.reason ?? '')
    }).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.message.set(result.remainingBalance <= 0 ? 'Loan settled and closed.' : `Loan settled. Remaining balance: ${result.remainingBalance.toFixed(2)}`);
        this.viewInstallments(loanId);
        this.loadBaseData();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to settle loan.'));
      }
    });
  }
}
