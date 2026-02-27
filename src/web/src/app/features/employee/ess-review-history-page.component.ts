import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { apiConfig } from '../../core/config/api.config';
import { I18nService } from '../../core/services/i18n.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

type EssReviewItem = {
  id: string;
  employeeId: string;
  employeeName: string;
  email: string;
  requestType: string;
  status: string;
  payloadJson: string;
  reviewerUserId?: string | null;
  reviewedAtUtc?: string | null;
  resolutionNotes?: string | null;
  createdAtUtc: string;
};

@Component({
  selector: 'app-ess-review-history-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './ess-review-history-page.component.html',
  styleUrl: './ess-review-history-page.component.scss'
})
export class EssReviewHistoryPageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  readonly i18n = inject(I18nService);
  private readonly base = `${apiConfig.baseUrl}/api/self-service/requests`;

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly items = signal<EssReviewItem[]>([]);
  readonly selected = signal<EssReviewItem | null>(null);
  readonly reviewNotes = signal('');

  statusFilter = 'All';
  typeFilter = 'All';
  take = 200;

  readonly payloadPreview = computed(() => {
    const item = this.selected();
    if (!item?.payloadJson) {
      return '{}';
    }

    try {
      return JSON.stringify(JSON.parse(item.payloadJson), null, 2);
    } catch {
      return item.payloadJson;
    }
  });

  ngOnInit(): void {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');
    this.message.set('');

    const query = new URLSearchParams();
    if (this.statusFilter !== 'All') {
      query.set('status', this.statusFilter);
    }
    if (this.typeFilter !== 'All') {
      query.set('requestType', this.typeFilter);
    }
    query.set('take', String(this.take || 200));

    this.http.get<any>(`${this.base}?${query.toString()}`).subscribe({
      next: (response) => {
        const rows = Array.isArray(response?.items) ? response.items : [];
        const mapped = rows.map((x: any) => ({
          id: String(x.id ?? ''),
          employeeId: String(x.employeeId ?? ''),
          employeeName: String(x.employeeName ?? '-'),
          email: String(x.email ?? ''),
          requestType: String(x.requestType ?? ''),
          status: String(x.status ?? ''),
          payloadJson: String(x.payloadJson ?? '{}'),
          reviewerUserId: x.reviewerUserId ? String(x.reviewerUserId) : null,
          reviewedAtUtc: x.reviewedAtUtc ? String(x.reviewedAtUtc) : null,
          resolutionNotes: x.resolutionNotes ? String(x.resolutionNotes) : null,
          createdAtUtc: String(x.createdAtUtc ?? '')
        })) as EssReviewItem[];
        this.items.set(mapped);

        const current = this.selected();
        if (current) {
          const updated = mapped.find((x) => x.id === current.id) ?? null;
          this.selected.set(updated);
        } else if (mapped.length > 0) {
          this.selected.set(mapped[0]);
        } else {
          this.selected.set(null);
        }
      },
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to load ESS review history.')),
      complete: () => this.loading.set(false)
    });
  }

  selectItem(item: EssReviewItem) {
    this.selected.set(item);
    this.reviewNotes.set(item.resolutionNotes ?? '');
  }

  reviewSelected(approved: boolean) {
    const item = this.selected();
    if (!item || item.status !== 'Submitted' || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.message.set('');

    this.http.post(`${apiConfig.baseUrl}/api/self-service/requests/${item.id}/review`, {
      approved,
      resolutionNotes: this.reviewNotes().trim() || (approved ? 'Approved by HR.' : 'Rejected by HR.')
    }).subscribe({
      next: () => {
        this.message.set(
          approved
            ? this.i18n.text('Request approved.', 'تم اعتماد الطلب.')
            : this.i18n.text('Request rejected.', 'تم رفض الطلب.')
        );
        this.load();
      },
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to review ESS request.')),
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
}
