import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LeaveRequest } from '../../core/models/leave.models';
import { I18nService } from '../../core/services/i18n.service';
import { LeaveService } from '../../core/services/leave.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';
import { EmployeesService } from '../../core/services/employees.service';
import { Employee } from '../../core/models/employee.models';

@Component({
  selector: 'app-leave-approvals-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './leave-approvals-page.component.html',
  styleUrl: './leave-approvals-page.component.scss'
})
export class LeaveApprovalsPageComponent implements OnInit {
  private readonly leaveService = inject(LeaveService);
  private readonly employeesService = inject(EmployeesService);
  readonly i18n = inject(I18nService);

  readonly requests = signal<LeaveRequest[]>([]);
  readonly employees = signal<Employee[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly statusFilter = signal(1);
  readonly selectedEmployeeId = signal('');
  readonly balanceYear = signal(new Date().getFullYear());
  readonly balanceLeaveType = signal(1);
  readonly allocatedDays = signal(21);
  readonly usedDays = signal(0);

  ngOnInit(): void {
    this.loadEmployees();
    this.loadRequests();
  }

  loadEmployees() {
    this.employeesService.list(1, 500).subscribe({
      next: (rows) => {
        this.employees.set(rows);
        if (!this.selectedEmployeeId() && rows.length > 0) {
          this.selectedEmployeeId.set(rows[0].id);
        }
      },
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load employees.'))
    });
  }

  loadRequests() {
    this.loading.set(true);
    this.error.set('');
    this.leaveService.listRequests(this.statusFilter()).subscribe({
      next: (rows) => {
        this.requests.set(rows);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load leave requests.'));
      }
    });
  }

  setStatusFilter(status: number) {
    this.statusFilter.set(status);
    this.loadRequests();
  }

  approve(requestId: string) {
    if (this.busy()) return;
    const comment = window.prompt(this.i18n.text('Optional approval comment', 'تعليق اعتماد اختياري')) ?? '';
    this.busy.set(true);
    this.message.set('');
    this.error.set('');

    this.leaveService.approveRequest(requestId, comment.trim()).subscribe({
      next: () => {
        this.busy.set(false);
        this.message.set('Leave request approved.');
        this.loadRequests();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to approve leave request.'));
      }
    });
  }

  reject(requestId: string) {
    if (this.busy()) return;
    const reason = window.prompt(this.i18n.text('Enter rejection reason', 'أدخل سبب الرفض'));
    if (!reason || !reason.trim()) return;

    this.busy.set(true);
    this.message.set('');
    this.error.set('');

    this.leaveService.rejectRequest(requestId, reason.trim()).subscribe({
      next: () => {
        this.busy.set(false);
        this.message.set('Leave request rejected.');
        this.loadRequests();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to reject leave request.'));
      }
    });
  }

  upsertBalance() {
    if (this.busy()) return;
    const employeeId = this.selectedEmployeeId();
    if (!employeeId) {
      this.error.set(this.i18n.text('Select employee first.', 'اختر الموظف أولاً.'));
      return;
    }

    this.busy.set(true);
    this.message.set('');
    this.error.set('');

    this.leaveService.upsertBalance({
      employeeId,
      year: Number(this.balanceYear()),
      leaveType: Number(this.balanceLeaveType()),
      allocatedDays: Number(this.allocatedDays()),
      usedDays: Number(this.usedDays())
    }).subscribe({
      next: () => {
        this.busy.set(false);
        this.message.set(this.i18n.text('Leave balance saved.', 'تم حفظ رصيد الإجازة.'));
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to save leave balance.'));
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

