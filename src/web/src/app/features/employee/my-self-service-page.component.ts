import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { apiConfig } from '../../core/config/api.config';
import { I18nService } from '../../core/services/i18n.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

type SelfServiceItem = {
  id: string;
  requestType: string;
  status: string;
  payloadJson: string;
  reviewerUserId?: string | null;
  reviewedAtUtc?: string | null;
  resolutionNotes?: string | null;
  createdAtUtc: string;
};

@Component({
  selector: 'app-my-self-service-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './my-self-service-page.component.html',
  styleUrl: './my-self-service-page.component.scss'
})
export class MySelfServicePageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  readonly i18n = inject(I18nService);
  private readonly base = `${apiConfig.baseUrl}/api/me/self-service/requests`;

  readonly items = signal<SelfServiceItem[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  requestType = 'ProfileUpdate';
  readonly profileForm = {
    firstName: '',
    lastName: '',
    email: '',
    jobTitle: '',
    gradeCode: '',
    locationCode: '',
    bankName: '',
    bankIban: '',
    iqamaNumber: '',
    iqamaExpiryDate: '',
    workPermitExpiryDate: '',
    contractEndDate: ''
  };
  readonly leaveForm = {
    leaveType: 'Annual',
    startDate: '',
    endDate: '',
    reason: ''
  };
  readonly loanForm = {
    loanType: 'Advance',
    principalAmount: 0,
    installmentAmount: 0,
    totalInstallments: 1,
    startYear: new Date().getFullYear(),
    startMonth: new Date().getMonth() + 1,
    notes: ''
  };
  readonly contractRenewalForm = {
    newContractEndDate: '',
    notes: ''
  };

  ngOnInit(): void {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');
    this.http.get<any>(this.base).subscribe({
      next: (response) => {
        const rows = Array.isArray(response?.items) ? response.items : [];
        this.items.set(rows.map((x: any) => ({
          id: String(x.id ?? ''),
          requestType: String(x.requestType ?? ''),
          status: String(x.status ?? ''),
          payloadJson: String(x.payloadJson ?? '{}'),
          reviewerUserId: x.reviewerUserId ? String(x.reviewerUserId) : null,
          reviewedAtUtc: x.reviewedAtUtc ? String(x.reviewedAtUtc) : null,
          resolutionNotes: x.resolutionNotes ? String(x.resolutionNotes) : null,
          createdAtUtc: String(x.createdAtUtc ?? '')
        })));
      },
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load self-service requests.')),
      complete: () => this.loading.set(false)
    });
  }

  submit() {
    if (this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.message.set('');

    const payloadJson = this.buildPayloadJson();
    if (!payloadJson) {
      return;
    }

    this.http.post(this.base, {
      requestType: this.requestType,
      payloadJson
    }).subscribe({
      next: () => {
        this.message.set(this.i18n.text('Request submitted.', 'تم إرسال الطلب.'));
        this.load();
      },
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to submit request.')),
      complete: () => this.saving.set(false)
    });
  }

  requestTypeLabel(value: string): string {
    const type = String(value || '').trim().toUpperCase();
    switch (type) {
      case 'PROFILEUPDATE':
        return this.i18n.text('Profile Update', 'تحديث الملف');
      case 'LEAVEREQUEST':
        return this.i18n.text('Leave Request', 'طلب إجازة');
      case 'LOANREQUEST':
        return this.i18n.text('Loan Request', 'طلب قرض');
      case 'CONTRACTRENEWAL':
        return this.i18n.text('Contract Renewal', 'تجديد عقد');
      default:
        return value || '-';
    }
  }

  statusLabel(value: string): string {
    const status = String(value || '').trim().toUpperCase();
    switch (status) {
      case 'SUBMITTED':
        return this.i18n.text('Submitted', 'مرسل');
      case 'APPROVED':
        return this.i18n.text('Approved', 'معتمد');
      case 'REJECTED':
        return this.i18n.text('Rejected', 'مرفوض');
      default:
        return value || '-';
    }
  }

  private buildPayloadJson(): string | null {
    if (this.requestType === 'ProfileUpdate') {
      const payload: Record<string, string> = {};
      this.addIfValue(payload, 'firstName', this.profileForm.firstName);
      this.addIfValue(payload, 'lastName', this.profileForm.lastName);
      this.addIfValue(payload, 'email', this.profileForm.email);
      this.addIfValue(payload, 'jobTitle', this.profileForm.jobTitle);
      this.addIfValue(payload, 'gradeCode', this.profileForm.gradeCode);
      this.addIfValue(payload, 'locationCode', this.profileForm.locationCode);
      this.addIfValue(payload, 'bankName', this.profileForm.bankName);
      this.addIfValue(payload, 'bankIban', this.profileForm.bankIban);
      this.addIfValue(payload, 'iqamaNumber', this.profileForm.iqamaNumber);
      this.addIfValue(payload, 'iqamaExpiryDate', this.profileForm.iqamaExpiryDate);
      this.addIfValue(payload, 'workPermitExpiryDate', this.profileForm.workPermitExpiryDate);
      this.addIfValue(payload, 'contractEndDate', this.profileForm.contractEndDate);

      if (Object.keys(payload).length === 0) {
        this.error.set(this.i18n.text('Add at least one field to update.', 'أدخل حقلًا واحدًا على الأقل للتحديث.'));
        return null;
      }

      return JSON.stringify(payload);
    }

    if (this.requestType === 'LeaveRequest') {
      if (!this.leaveForm.startDate || !this.leaveForm.endDate) {
        this.error.set(this.i18n.text('Start and end dates are required.', 'تاريخ البداية والنهاية مطلوبان.'));
        return null;
      }

      if (this.leaveForm.endDate < this.leaveForm.startDate) {
        this.error.set(this.i18n.text('End date cannot be before start date.', 'تاريخ النهاية لا يمكن أن يكون قبل البداية.'));
        return null;
      }

      return JSON.stringify({
        leaveType: this.leaveForm.leaveType,
        startDate: this.leaveForm.startDate,
        endDate: this.leaveForm.endDate,
        reason: this.leaveForm.reason || 'Submitted from ESS portal.'
      });
    }

    if (this.requestType === 'LoanRequest') {
      if (this.loanForm.principalAmount <= 0 || this.loanForm.installmentAmount <= 0 || this.loanForm.totalInstallments <= 0) {
        this.error.set(this.i18n.text('Loan amounts and installments must be greater than zero.', 'مبالغ القرض وعدد الأقساط يجب أن تكون أكبر من صفر.'));
        return null;
      }

      if (this.loanForm.startMonth < 1 || this.loanForm.startMonth > 12) {
        this.error.set(this.i18n.text('Start month must be between 1 and 12.', 'شهر البداية يجب أن يكون بين 1 و12.'));
        return null;
      }

      return JSON.stringify({
        loanType: this.loanForm.loanType,
        principalAmount: Number(this.loanForm.principalAmount),
        installmentAmount: Number(this.loanForm.installmentAmount),
        totalInstallments: Number(this.loanForm.totalInstallments),
        startYear: Number(this.loanForm.startYear),
        startMonth: Number(this.loanForm.startMonth),
        notes: this.loanForm.notes || ''
      });
    }

    if (this.requestType === 'ContractRenewal') {
      if (!this.contractRenewalForm.newContractEndDate) {
        this.error.set(this.i18n.text('New contract end date is required.', 'تاريخ نهاية العقد الجديد مطلوب.'));
        return null;
      }

      return JSON.stringify({
        newContractEndDate: this.contractRenewalForm.newContractEndDate,
        notes: this.contractRenewalForm.notes || ''
      });
    }

    this.error.set(this.i18n.text('Unsupported request type.', 'نوع الطلب غير مدعوم.'));
    return null;
  }

  private addIfValue(target: Record<string, string>, key: string, value: string) {
    const cleaned = String(value || '').trim();
    if (cleaned) {
      target[key] = cleaned;
    }
  }
}
