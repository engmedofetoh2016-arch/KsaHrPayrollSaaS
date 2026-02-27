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
  payloadJson = '{\n  "notes": ""\n}';

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

    this.http.post(this.base, {
      requestType: this.requestType,
      payloadJson: this.payloadJson
    }).subscribe({
      next: () => {
        this.message.set(this.i18n.text('Request submitted.', 'تم إرسال الطلب.'));
        this.load();
      },
      error: (err) => this.error.set(getApiErrorMessage(err, 'Failed to submit request.')),
      complete: () => this.saving.set(false)
    });
  }
}
