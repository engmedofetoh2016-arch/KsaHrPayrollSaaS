import { CommonModule } from '@angular/common';
import { HttpResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { LeaveAttachment, LeaveBalance, LeaveBalancePreviewResult, LeaveRequest } from '../../core/models/leave.models';
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
  readonly previewLoading = signal(false);
  readonly attachmentBusyRequestId = signal<string | null>(null);
  readonly message = signal('');
  readonly error = signal('');
  readonly currentYear = signal(new Date().getFullYear());
  readonly isEmployee = computed(() => this.authService.hasAnyRole(['Employee']));
  readonly preview = signal<LeaveBalancePreviewResult | null>(null);
  readonly selectedRequestIdForAttachments = signal<string | null>(null);
  readonly attachments = signal<LeaveAttachment[]>([]);

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

    const preview = this.preview();
    if (preview && !preview.canSubmit) {
      this.error.set('Cannot submit leave request with insufficient balance.');
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
          this.preview.set(null);
          this.loadData();
        },
        error: (err) => {
          this.busy.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to submit leave request.'));
        }
      });
  }

  previewBalance() {
    if (this.requestForm.invalid || this.previewLoading()) {
      this.requestForm.markAllAsTouched();
      return;
    }

    const v = this.requestForm.getRawValue();
    this.previewLoading.set(true);
    this.error.set('');

    this.leaveService.previewBalance({
      leaveType: Number(v.leaveType),
      startDate: String(v.startDate),
      endDate: String(v.endDate)
    }).subscribe({
      next: (result) => {
        this.previewLoading.set(false);
        this.preview.set(result);
      },
      error: (err) => {
        this.previewLoading.set(false);
        this.preview.set(null);
        this.error.set(getApiErrorMessage(err, 'Failed to preview leave balance.'));
      }
    });
  }

  selectAttachments(requestId: string) {
    if (this.selectedRequestIdForAttachments() === requestId) {
      this.selectedRequestIdForAttachments.set(null);
      this.attachments.set([]);
      return;
    }

    this.selectedRequestIdForAttachments.set(requestId);
    this.loadAttachments(requestId);
  }

  private loadAttachments(requestId: string) {
    this.attachments.set([]);
    this.leaveService.listAttachments(requestId).subscribe({
      next: (rows) => this.attachments.set(rows),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load attachments.'))
    });
  }

  uploadAttachment(requestId: string, event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.attachmentBusyRequestId.set(requestId);
    this.error.set('');
    this.message.set('');

    this.leaveService.uploadAttachment(requestId, file).subscribe({
      next: () => {
        this.attachmentBusyRequestId.set(null);
        this.message.set('Attachment uploaded.');
        if (this.selectedRequestIdForAttachments() === requestId) {
          this.loadAttachments(requestId);
        }
        input.value = '';
      },
      error: (err) => {
        this.attachmentBusyRequestId.set(null);
        this.error.set(getApiErrorMessage(err, 'Failed to upload attachment.'));
      }
    });
  }

  downloadAttachment(attachmentId: string) {
    this.leaveService.downloadAttachment(attachmentId).subscribe({
      next: (response) => this.downloadFileFromResponse(response, 'leave-attachment.bin'),
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to download attachment.'))
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

  private downloadFileFromResponse(response: HttpResponse<Blob>, fallbackFileName: string) {
    const blob = response.body;
    if (!blob) {
      this.error.set('Attachment response is empty.');
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
