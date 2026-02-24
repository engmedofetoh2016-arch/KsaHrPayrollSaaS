import { CommonModule } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { catchError, of } from 'rxjs';
import { apiConfig } from '../../core/config/api.config';
import { I18nService } from '../../core/services/i18n.service';

type RegistryItem = {
  id: string;
  createdAtUtc: string;
  decisionType: string;
  userId?: string;
  runId?: string;
  category?: string;
  referenceId?: string;
  criticalCodes?: string;
  warningCount?: string;
  reason?: string;
};

@Component({
  selector: 'app-reference-registry-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './reference-registry-page.component.html',
  styleUrl: './reference-registry-page.component.scss'
})
export class ReferenceRegistryPageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  readonly i18n = inject(I18nService);
  private readonly base = `${apiConfig.baseUrl}/api/payroll/governance/decisions`;
  private readonly exportBase = `${apiConfig.baseUrl}/api/payroll/governance/decisions/export-csv`;

  readonly loading = signal(false);
  readonly error = signal('');
  readonly items = signal<RegistryItem[]>([]);
  readonly total = signal(0);
  readonly skip = signal(0);
  readonly take = signal(50);
  readonly hasMore = signal(false);
  readonly exporting = signal(false);

  days = 30;
  referenceId = '';
  category = 'All';
  criticalCode = '';
  runId = '';

  readonly selectedCountLabel = computed(
    () => `${this.items().length}/${this.total()}`
  );

  ngOnInit(): void {
    this.load(true);
  }

  load(reset: boolean) {
    const nextSkip = reset ? 0 : this.skip();
    this.loading.set(true);
    this.error.set('');

    const params = this.buildBaseParams()
      .set('skip', String(nextSkip))
      .set('take', String(this.take()));

    this.http.get<any>(this.base, { params }).pipe(catchError(() => of(null))).subscribe({
      next: (response) => {
        if (!response) {
          this.error.set(this.i18n.text('Failed to load reference registry.', 'تعذر تحميل سجل المراجع.'));
          return;
        }

        const rows: RegistryItem[] = Array.isArray(response.items)
          ? response.items.map((x: any) => ({
              id: String(x.id ?? ''),
              createdAtUtc: String(x.createdAtUtc ?? ''),
              decisionType: String(x.decisionType ?? ''),
              userId: x.userId ? String(x.userId) : undefined,
              runId: x.runId ? String(x.runId) : undefined,
              category: x.category ? String(x.category) : undefined,
              referenceId: x.referenceId ? String(x.referenceId) : undefined,
              criticalCodes: x.criticalCodes ? String(x.criticalCodes) : undefined,
              warningCount: x.warningCount ? String(x.warningCount) : undefined,
              reason: x.reason ? String(x.reason) : undefined
            }))
          : [];

        this.items.set(reset ? rows : [...this.items(), ...rows]);
        this.total.set(Number(response.total ?? rows.length));
        this.skip.set(nextSkip + rows.length);
        this.hasMore.set(!!response.hasMore);
      },
      complete: () => this.loading.set(false)
    });
  }

  applyFilters() {
    this.load(true);
  }

  clearFilters() {
    this.days = 30;
    this.referenceId = '';
    this.category = 'All';
    this.criticalCode = '';
    this.runId = '';
    this.load(true);
  }

  loadMore() {
    if (this.loading() || !this.hasMore()) {
      return;
    }
    this.load(false);
  }

  exportCsv() {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);
    this.error.set('');

    const params = this.buildBaseParams();
    this.http.get(this.exportBase, { params, responseType: 'blob', observe: 'response' }).subscribe({
      next: (response) => {
        const disposition = response.headers.get('content-disposition') ?? '';
        const match = /filename="?([^"]+)"?/i.exec(disposition);
        const fileName = match?.[1] ?? `reference-registry-${new Date().toISOString().slice(0, 10)}.csv`;
        this.downloadBlob(response.body, fileName);
      },
      error: () => {
        this.error.set(this.i18n.text('Failed to export registry CSV.', 'تعذر تصدير CSV لسجل المراجع.'));
      },
      complete: () => this.exporting.set(false)
    });
  }

  private buildBaseParams(): HttpParams {
    let params = new HttpParams().set('days', String(this.days));

    const referenceId = this.referenceId.trim();
    const category = this.category;
    const criticalCode = this.criticalCode.trim();
    const runId = this.runId.trim();

    if (referenceId) {
      params = params.set('referenceId', referenceId);
    }
    if (category && category !== 'All') {
      params = params.set('category', category);
    }
    if (criticalCode) {
      params = params.set('criticalCode', criticalCode);
    }
    if (runId) {
      params = params.set('runId', runId);
    }

    return params;
  }

  private downloadBlob(blob: Blob | null, fileName: string) {
    if (!blob) {
      return;
    }

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
