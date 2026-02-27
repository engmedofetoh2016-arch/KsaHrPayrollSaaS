import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AttendanceInputRow } from '../../core/models/attendance.models';
import { Employee } from '../../core/models/employee.models';
import { AttendanceService } from '../../core/services/attendance.service';
import { EmployeesService } from '../../core/services/employees.service';
import { I18nService } from '../../core/services/i18n.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-attendance-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './attendance-page.component.html',
  styleUrl: './attendance-page.component.scss'
})
export class AttendancePageComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly attendanceService = inject(AttendanceService);
  private readonly employeesService = inject(EmployeesService);
  readonly i18n = inject(I18nService);

  readonly rows = signal<AttendanceInputRow[]>([]);
  readonly employees = signal<Employee[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  readonly filterForm = this.fb.group({
    year: [new Date().getFullYear(), [Validators.required, Validators.min(2000), Validators.max(2100)]],
    month: [new Date().getMonth() + 1, [Validators.required, Validators.min(1), Validators.max(12)]]
  });

  readonly form = this.fb.group({
    employeeId: ['', [Validators.required]],
    daysPresent: [0, [Validators.required, Validators.min(0)]],
    daysAbsent: [0, [Validators.required, Validators.min(0)]],
    overtimeHours: [0, [Validators.required, Validators.min(0)]]
  });

  ngOnInit(): void {
    this.loadEmployees();
    this.loadRows();
  }

  loadEmployees() {
    this.employeesService.list().subscribe({
      next: (employees) => {
        this.employees.set(employees);

        const currentEmployeeId = String(this.form.getRawValue().employeeId ?? '').trim();
        const exists = employees.some((x) => x.id === currentEmployeeId);
        if (!exists) {
          this.form.patchValue({ employeeId: employees[0]?.id ?? '' });
        }
      },
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load employees for attendance input.'))
    });
  }

  loadRows() {
    const filter = this.filterForm.getRawValue();
    const year = Number(filter.year ?? new Date().getFullYear());
    const month = Number(filter.month ?? new Date().getMonth() + 1);

    this.loading.set(true);
    this.error.set('');

    this.attendanceService.list(year, month).subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load attendance inputs.'));
      }
    });
  }

  save() {
    if (this.employees().length === 0) {
      this.error.set(this.i18n.text('No employees available. Add employees first.', 'لا يوجد موظفون متاحون. أضف موظفين أولًا.'));
      return;
    }

    if (this.form.invalid || this.filterForm.invalid || this.saving()) {
      this.form.markAllAsTouched();
      this.filterForm.markAllAsTouched();
      return;
    }

    const filter = this.filterForm.getRawValue();
    const value = this.form.getRawValue();

    this.saving.set(true);
    this.message.set('');
    this.error.set('');

    this.attendanceService
      .upsert({
        employeeId: value.employeeId ?? '',
        year: Number(filter.year),
        month: Number(filter.month),
        daysPresent: Number(value.daysPresent ?? 0),
        daysAbsent: Number(value.daysAbsent ?? 0),
        overtimeHours: Number(value.overtimeHours ?? 0)
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.message.set('Attendance saved.');
          this.loadRows();
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to save attendance input.'));
        }
      });
  }
}
