import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LeaveBalance, LeaveRequest } from '../../core/models/leave.models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { LeaveService } from '../../core/services/leave.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-my-leave-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './my-leave-page.component.html',
  styleUrl: './my-leave-page.component.scss'
})
export class MyLeavePageComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly leaveService = inject(LeaveService);
  private readonly authService = inject(AuthService);
  readonly i18n = inject(I18nService);

  readonly requests = signal<LeaveRequest[]>([]);
  readonly balances = signal<LeaveBalance[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly currentYear = signal(new Date().getFullYear());
  readonly isEmployee = computed(() => this.authService.hasAnyRole(['Employee']));

  readonly requestForm = this.fb.group({
    leaveType: [1, [Validators.required]],
    startDate: ['', [Validators.required]],
    endDate: ['', [Validators.required]],
    reason: ['', [Validators.required, Validators.maxLength(500)]]
  });

  ngOnInit(): void {
    this.loadData();
  }

  loadData() {
    this.loading.set(true);
    this.error.set('');

    this.leaveService.listRequests().subscribe({
      next: (rows) => {
        this.requests.set(rows);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load leave requests.'));
      }
    });

    this.leaveService.listBalances(this.currentYear()).subscribe({
      next: (rows) => this.balances.set(rows),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load leave balances.'))
    });
  }

  submitRequest() {
    if (this.requestForm.invalid || this.busy()) {
      this.requestForm.markAllAsTouched();
      return;
    }

    const v = this.requestForm.getRawValue();
    this.busy.set(true);
    this.message.set('');
    this.error.set('');

    this.leaveService
      .createRequest({
        leaveType: Number(v.leaveType),
        startDate: String(v.startDate),
        endDate: String(v.endDate),
        reason: String(v.reason)
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.message.set('Leave request submitted.');
          this.requestForm.patchValue({ reason: '' });
          this.loadData();
        },
        error: (err) => {
          this.busy.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to submit leave request.'));
        }
      });
  }

  leaveTypeLabel(value: number) {
    switch (value) {
      case 1:
        return this.i18n.text('Annual', 'سنوية');
      case 2:
        return this.i18n.text('Sick', 'مرضية');
      case 3:
        return this.i18n.text('Unpaid', 'غير مدفوعة');
      default:
        return this.i18n.text('Unknown', 'غير معروف');
    }
  }

  statusLabel(value: number) {
    switch (value) {
      case 1:
        return this.i18n.text('Pending', 'معلق');
      case 2:
        return this.i18n.text('Approved', 'معتمد');
      case 3:
        return this.i18n.text('Rejected', 'مرفوض');
      default:
        return this.i18n.text('Unknown', 'غير معروف');
    }
  }
}
