import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AttendanceInputRow, MyBookingRow, TimesheetBookingRow } from '../../core/models/attendance.models';
import { Employee } from '../../core/models/employee.models';
import { AuthService } from '../../core/services/auth.service';
import { AttendanceService } from '../../core/services/attendance.service';
import { EmployeesService } from '../../core/services/employees.service';
import { I18nService } from '../../core/services/i18n.service';
import { MeService } from '../../core/services/me.service';
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
  private readonly auth = inject(AuthService);
  private readonly meService = inject(MeService);
  private readonly attendanceService = inject(AttendanceService);
  private readonly employeesService = inject(EmployeesService);
  readonly i18n = inject(I18nService);
  readonly isEmployee = computed(() => this.auth.hasAnyRole(['Employee']));

  readonly rows = signal<AttendanceInputRow[]>([]);
  readonly employees = signal<Employee[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  readonly bookingRows = signal<MyBookingRow[]>([]);
  readonly bookingBusy = signal(false);
  readonly bookingError = signal('');
  readonly bookingMessage = signal('');

  readonly dailyBookingRows = signal<TimesheetBookingRow[]>([]);
  readonly dailyBookingLoading = signal(false);
  readonly dailyBookingError = signal('');
  readonly selectedDetailsEmployeeId = signal('');
  readonly selectedDetailsEmployeeName = signal('');
  readonly selectedEmployeeDailyRows = computed(() => {
    const employeeId = this.selectedDetailsEmployeeId();
    if (!employeeId) {
      return [] as TimesheetBookingRow[];
    }

    return this.dailyBookingRows()
      .filter((x) => x.employeeId === employeeId)
      .sort((a, b) => a.workDate.localeCompare(b.workDate));
  });

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

  readonly manualBookingForm = this.fb.group({
    workDate: [new Date().toISOString().slice(0, 10), [Validators.required]],
    checkInLocal: [''],
    checkOutLocal: [''],
    hoursWorked: [null as number | null, [Validators.min(0)]],
    notes: ['']
  });

  ngOnInit(): void {
    this.loadEmployees();
    if (this.isEmployee()) {
      this.loadMyBookings();
      return;
    }

    this.loadRows();
  }

  loadEmployees() {
    if (this.isEmployee()) {
      this.meService.getProfile().subscribe({
        next: (profile) => {
          const employee = profile.employee;
          if (!employee) {
            this.error.set(getApiErrorMessage(null, 'No employee profile linked to this account.'));
            this.employees.set([]);
            this.form.patchValue({ employeeId: '' });
            return;
          }

          const selfEmployee: Employee = {
            id: employee.id,
            startDate: employee.startDate,
            firstName: employee.firstName,
            lastName: employee.lastName,
            email: employee.email,
            jobTitle: employee.jobTitle,
            baseSalary: employee.baseSalary,
            employeeNumber: employee.employeeNumber,
            bankName: employee.bankName,
            bankIban: employee.bankIban,
            iqamaNumber: employee.iqamaNumber,
            iqamaExpiryDate: employee.iqamaExpiryDate ?? null,
            workPermitExpiryDate: employee.workPermitExpiryDate ?? null,
            contractEndDate: employee.contractEndDate ?? null,
            tenantId: profile.user.tenantId,
            gradeCode: undefined,
            locationCode: undefined,
            isSaudiNational: false,
            isGosiEligible: false,
            gosiBasicWage: 0,
            gosiHousingAllowance: 0
          };

          this.employees.set([selfEmployee]);
          this.form.patchValue({ employeeId: employee.id });
        },
        error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load employee profile for attendance input.'))
      });
      return;
    }

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
        if (this.selectedDetailsEmployeeId() && !rows.some((x) => x.employeeId === this.selectedDetailsEmployeeId())) {
          this.selectedDetailsEmployeeId.set('');
          this.selectedDetailsEmployeeName.set('');
        }
        this.loading.set(false);
        this.loadDailyBookings();
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

  loadMyBookings() {
    this.bookingBusy.set(true);
    this.bookingError.set('');
    this.bookingMessage.set('');

    const filter = this.filterForm.getRawValue();
    const year = Number(filter.year ?? new Date().getFullYear());
    const month = Number(filter.month ?? new Date().getMonth() + 1);
    const from = `${year}-${String(month).padStart(2, '0')}-01`;
    const to = new Date(year, month, 0).toISOString().slice(0, 10);

    this.attendanceService.listMyBookings(from, to).subscribe({
      next: (rows) => {
        this.bookingRows.set(rows);
        this.bookingBusy.set(false);
      },
      error: (err) => {
        this.bookingBusy.set(false);
        this.bookingError.set(getApiErrorMessage(err, 'Failed to load bookings.'));
      }
    });
  }

  checkIn() {
    if (this.bookingBusy()) {
      return;
    }

    this.bookingBusy.set(true);
    this.bookingError.set('');
    this.bookingMessage.set('');

    this.attendanceService.checkIn().subscribe({
      next: () => {
        this.bookingBusy.set(false);
        this.bookingMessage.set(this.i18n.text('Checked in successfully.', 'تم تسجيل الحضور بنجاح.'));
        this.loadMyBookings();
      },
      error: (err) => {
        this.bookingBusy.set(false);
        this.bookingError.set(getApiErrorMessage(err, 'Failed to check in.'));
      }
    });
  }

  checkOut() {
    if (this.bookingBusy()) {
      return;
    }

    this.bookingBusy.set(true);
    this.bookingError.set('');
    this.bookingMessage.set('');

    this.attendanceService.checkOut().subscribe({
      next: () => {
        this.bookingBusy.set(false);
        this.bookingMessage.set(this.i18n.text('Checked out successfully.', 'تم تسجيل الانصراف بنجاح.'));
        this.loadMyBookings();
      },
      error: (err) => {
        this.bookingBusy.set(false);
        this.bookingError.set(getApiErrorMessage(err, 'Failed to check out.'));
      }
    });
  }

  saveManualBooking() {
    if (this.bookingBusy() || this.manualBookingForm.invalid) {
      this.manualBookingForm.markAllAsTouched();
      return;
    }

    const value = this.manualBookingForm.getRawValue();
    const checkInUtc = value.checkInLocal ? new Date(value.checkInLocal).toISOString() : null;
    const checkOutUtc = value.checkOutLocal ? new Date(value.checkOutLocal).toISOString() : null;

    this.bookingBusy.set(true);
    this.bookingError.set('');
    this.bookingMessage.set('');

    this.attendanceService.saveManualBooking({
      workDate: value.workDate ?? '',
      checkInUtc,
      checkOutUtc,
      hoursWorked: value.hoursWorked ?? null,
      notes: (value.notes ?? '').trim() || null
    }).subscribe({
      next: () => {
        this.bookingBusy.set(false);
        this.bookingMessage.set(this.i18n.text('Manual booking saved.', 'Manual booking saved.'));
        this.loadMyBookings();
      },
      error: (err) => {
        this.bookingBusy.set(false);
        this.bookingError.set(getApiErrorMessage(err, 'Failed to save manual booking.'));
      }
    });
  }

  showEmployeeDetails(row: AttendanceInputRow) {
    this.selectedDetailsEmployeeId.set(row.employeeId);
    this.selectedDetailsEmployeeName.set(row.employeeName);
  }

  clearEmployeeDetails() {
    this.selectedDetailsEmployeeId.set('');
    this.selectedDetailsEmployeeName.set('');
  }

  private loadDailyBookings() {
    if (this.isEmployee()) {
      return;
    }

    const filter = this.filterForm.getRawValue();
    const year = Number(filter.year ?? new Date().getFullYear());
    const month = Number(filter.month ?? new Date().getMonth() + 1);

    this.dailyBookingLoading.set(true);
    this.dailyBookingError.set('');

    this.attendanceService.listTimesheets(year, month).subscribe({
      next: (rows) => {
        this.dailyBookingRows.set(rows);
        this.dailyBookingLoading.set(false);
      },
      error: (err) => {
        this.dailyBookingLoading.set(false);
        this.dailyBookingError.set(getApiErrorMessage(err, 'Failed to load daily bookings.'));
      }
    });
  }
}
