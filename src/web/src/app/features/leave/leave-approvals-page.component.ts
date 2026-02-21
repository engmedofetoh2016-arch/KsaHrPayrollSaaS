import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { LeaveRequest } from '../../core/models/leave.models';
import { I18nService } from '../../core/services/i18n.service';
import { LeaveService } from '../../core/services/leave.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-leave-approvals-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './leave-approvals-page.component.html',
  styleUrl: './leave-approvals-page.component.scss'
})
export class LeaveApprovalsPageComponent implements OnInit {
  private readonly leaveService = inject(LeaveService);
  readonly i18n = inject(I18nService);

  readonly requests = signal<LeaveRequest[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly statusFilter = signal(1);

  ngOnInit(): void {
    this.loadRequests();
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
    this.busy.set(true);
    this.message.set('');
    this.error.set('');

    this.leaveService.approveRequest(requestId).subscribe({
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
