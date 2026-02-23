import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MyPayslipItem } from '../../core/models/me.models';
import { I18nService } from '../../core/services/i18n.service';
import { MeService } from '../../core/services/me.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-my-payslips-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './my-payslips-page.component.html',
  styleUrl: './my-payslips-page.component.scss'
})
export class MyPayslipsPageComponent implements OnInit {
  private readonly meService = inject(MeService);
  readonly i18n = inject(I18nService);

  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly payslips = signal<MyPayslipItem[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');

    this.meService.listPayslips().subscribe({
      next: (rows) => {
        this.payslips.set(rows);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load payslips.'));
      }
    });
  }

  download(item: MyPayslipItem) {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set('');

    this.meService.downloadPayslip(item.id).subscribe({
      next: (blob) => {
        this.busy.set(false);
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = item.fileName || `payslip-${item.periodYear}-${String(item.periodMonth).padStart(2, '0')}.pdf`;
        link.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to download payslip.'));
      }
    });
  }

  statusLabel(status: number) {
    switch (status) {
      case 1:
        return this.i18n.text('Pending', 'قيد الانتظار');
      case 2:
        return this.i18n.text('Processing', 'قيد المعالجة');
      case 3:
        return this.i18n.text('Completed', 'مكتمل');
      case 4:
        return this.i18n.text('Failed', 'فشل');
      default:
        return this.i18n.text('Unknown', 'غير معروف');
    }
  }

  canDownload(status: number) {
    return status === 3;
  }
}
