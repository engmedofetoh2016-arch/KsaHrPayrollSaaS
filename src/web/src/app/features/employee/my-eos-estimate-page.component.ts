import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MyEosEstimateResponse } from '../../core/models/me.models';
import { I18nService } from '../../core/services/i18n.service';
import { MeService } from '../../core/services/me.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-my-eos-estimate-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './my-eos-estimate-page.component.html',
  styleUrl: './my-eos-estimate-page.component.scss'
})
export class MyEosEstimatePageComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly meService = inject(MeService);
  readonly i18n = inject(I18nService);

  readonly loading = signal(false);
  readonly error = signal('');
  readonly result = signal<MyEosEstimateResponse | null>(null);

  readonly form = this.fb.group({
    terminationDate: ['']
  });

  ngOnInit(): void {
    const today = new Date().toISOString().slice(0, 10);
    this.form.patchValue({ terminationDate: today });
    this.calculate();
  }

  calculate() {
    if (this.loading()) {
      return;
    }

    this.loading.set(true);
    this.error.set('');
    const terminationDate = String(this.form.getRawValue().terminationDate ?? '').trim();

    this.meService.getEosEstimate(terminationDate || null).subscribe({
      next: (res) => {
        this.result.set(res);
      },
      error: (err) => {
        this.error.set(getApiErrorMessage(err, 'Failed to calculate EOS estimate.'));
      },
      complete: () => this.loading.set(false)
    });
  }
}
