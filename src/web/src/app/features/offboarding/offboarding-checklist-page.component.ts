import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Employee } from '../../core/models/employee.models';
import { OffboardingChecklist, OffboardingRecord } from '../../core/models/offboarding.models';
import { EmployeesService } from '../../core/services/employees.service';
import { I18nService } from '../../core/services/i18n.service';
import { OffboardingService } from '../../core/services/offboarding.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-offboarding-checklist-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './offboarding-checklist-page.component.html',
  styleUrl: './offboarding-checklist-page.component.scss'
})
export class OffboardingChecklistPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly employeesService = inject(EmployeesService);
  private readonly offboardingService = inject(OffboardingService);
  readonly i18n = inject(I18nService);

  readonly employees = signal<Employee[]>([]);
  readonly offboardings = signal<OffboardingRecord[]>([]);
  readonly checklist = signal<OffboardingChecklist | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  readonly createForm = this.fb.group({
    employeeId: ['', [Validators.required]],
    effectiveDate: [new Date().toISOString().slice(0, 10), [Validators.required]],
    reason: ['', [Validators.required, Validators.maxLength(300)]]
  });

  readonly addItemForm = this.fb.group({
    itemCode: ['', [Validators.required, Validators.maxLength(50)]],
    itemLabel: ['', [Validators.required, Validators.maxLength(200)]],
    sortOrder: [100, [Validators.required, Validators.min(1), Validators.max(1000)]],
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
        if (!this.createForm.getRawValue().employeeId && rows.length > 0) {
          this.createForm.patchValue({ employeeId: rows[0].id });
        }
      },
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load employees.'))
    });

    this.offboardingService.list().subscribe({
      next: (rows) => {
        this.offboardings.set(rows);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load offboarding records.'));
      }
    });
  }

  createOffboarding() {
    if (this.createForm.invalid || this.saving()) {
      this.createForm.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    const v = this.createForm.getRawValue();
    this.offboardingService.create({
      employeeId: String(v.employeeId),
      effectiveDate: String(v.effectiveDate),
      reason: String(v.reason)
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.message.set('Offboarding created with default checklist.');
        this.loadBaseData();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to create offboarding.'));
      }
    });
  }

  approve(offboardingId: string) {
    this.saving.set(true);
    this.offboardingService.approve(offboardingId).subscribe({
      next: () => {
        this.saving.set(false);
        this.loadBaseData();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to approve offboarding.'));
      }
    });
  }

  loadChecklist(offboardingId: string) {
    this.checklist.set(null);
    this.error.set('');
    this.offboardingService.getChecklist(offboardingId).subscribe({
      next: (row) => this.checklist.set(row),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load checklist.'))
    });
  }

  addChecklistItem() {
    const current = this.checklist();
    if (!current || this.addItemForm.invalid || this.saving()) {
      this.addItemForm.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    const v = this.addItemForm.getRawValue();
    this.offboardingService.addChecklistItem(current.offboardingId, {
      itemCode: String(v.itemCode),
      itemLabel: String(v.itemLabel),
      sortOrder: Number(v.sortOrder),
      notes: String(v.notes ?? '')
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.addItemForm.patchValue({ itemCode: '', itemLabel: '', notes: '' });
        this.loadChecklist(current.offboardingId);
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to add checklist item.'));
      }
    });
  }

  completeItem(itemId: string) {
    const current = this.checklist();
    if (!current || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.offboardingService.completeChecklistItem(current.offboardingId, itemId).subscribe({
      next: () => {
        this.saving.set(false);
        this.loadChecklist(current.offboardingId);
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to complete checklist item.'));
      }
    });
  }

  reopenItem(itemId: string) {
    const current = this.checklist();
    if (!current || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.offboardingService.reopenChecklistItem(current.offboardingId, itemId).subscribe({
      next: () => {
        this.saving.set(false);
        this.loadChecklist(current.offboardingId);
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to reopen checklist item.'));
      }
    });
  }
}
