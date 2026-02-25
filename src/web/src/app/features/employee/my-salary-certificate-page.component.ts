import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { I18nService } from '../../core/services/i18n.service';
import { MeService } from '../../core/services/me.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-my-salary-certificate-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './my-salary-certificate-page.component.html',
  styleUrl: './my-salary-certificate-page.component.scss'
})
export class MySalaryCertificatePageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly meService = inject(MeService);
  readonly i18n = inject(I18nService);

  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  readonly form = this.fb.group({
    purpose: ['']
  });

  download() {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.message.set('');

    const purpose = String(this.form.getRawValue().purpose ?? '').trim();
    this.meService.downloadSalaryCertificate(purpose).subscribe({
      next: (blob) => {
        this.busy.set(false);
        this.message.set(this.i18n.text('Salary certificate downloaded.', 'تم تنزيل شهادة الراتب.'));
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `salary-certificate-${new Date().toISOString().slice(0, 10)}.pdf`;
        link.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to download salary certificate.'));
      }
    });
  }
}
