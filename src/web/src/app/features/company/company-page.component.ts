import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { CompanyService } from '../../core/services/company.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-company-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './company-page.component.html',
  styleUrl: './company-page.component.scss'
})
export class CompanyPageComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly companyService = inject(CompanyService);
  private readonly authService = inject(AuthService);
  readonly i18n = inject(I18nService);

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly canEdit = computed(() => this.authService.hasAnyRole(['Owner', 'Admin']));

  readonly form = this.fb.group({
    legalName: ['', [Validators.required, Validators.minLength(2)]],
    currencyCode: ['SAR', [Validators.required, Validators.minLength(3), Validators.maxLength(3)]],
    defaultPayDay: [25, [Validators.required, Validators.min(1), Validators.max(31)]],
    eosFirstFiveYearsMonthFactor: [0.5, [Validators.required, Validators.min(0), Validators.max(2)]],
    eosAfterFiveYearsMonthFactor: [1, [Validators.required, Validators.min(0), Validators.max(2)]],
    wpsCompanyBankName: [''],
    wpsCompanyBankCode: [''],
    wpsCompanyIban: [''],
    complianceDigestEnabled: [false],
    complianceDigestEmail: [''],
    complianceDigestFrequency: ['Weekly'],
    complianceDigestHourUtc: [6, [Validators.min(0), Validators.max(23)]]
  });

  ngOnInit(): void {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');

    this.companyService.getProfile().subscribe({
      next: (profile) => {
        this.form.patchValue({
          legalName: profile.legalName,
          currencyCode: profile.currencyCode,
          defaultPayDay: profile.defaultPayDay,
          eosFirstFiveYearsMonthFactor: profile.eosFirstFiveYearsMonthFactor,
          eosAfterFiveYearsMonthFactor: profile.eosAfterFiveYearsMonthFactor,
          wpsCompanyBankName: profile.wpsCompanyBankName ?? '',
          wpsCompanyBankCode: profile.wpsCompanyBankCode ?? '',
          wpsCompanyIban: profile.wpsCompanyIban ?? '',
          complianceDigestEnabled: !!profile.complianceDigestEnabled,
          complianceDigestEmail: profile.complianceDigestEmail ?? '',
          complianceDigestFrequency: profile.complianceDigestFrequency ?? 'Weekly',
          complianceDigestHourUtc: Number(profile.complianceDigestHourUtc ?? 6)
        });
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load company profile.'));
      }
    });
  }

  save() {
    if (!this.canEdit()) {
      this.error.set('Only Owner/Admin can update company profile.');
      return;
    }

    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.message.set('');
    this.error.set('');

    const value = this.form.getRawValue();
    this.companyService
      .updateProfile({
        legalName: value.legalName ?? '',
        currencyCode: value.currencyCode ?? 'SAR',
        defaultPayDay: Number(value.defaultPayDay ?? 25),
        eosFirstFiveYearsMonthFactor: Number(value.eosFirstFiveYearsMonthFactor ?? 0.5),
        eosAfterFiveYearsMonthFactor: Number(value.eosAfterFiveYearsMonthFactor ?? 1),
        wpsCompanyBankName: value.wpsCompanyBankName ?? '',
        wpsCompanyBankCode: value.wpsCompanyBankCode ?? '',
        wpsCompanyIban: value.wpsCompanyIban ?? '',
        complianceDigestEnabled: !!value.complianceDigestEnabled,
        complianceDigestEmail: value.complianceDigestEmail ?? '',
        complianceDigestFrequency: value.complianceDigestFrequency ?? 'Weekly',
        complianceDigestHourUtc: Number(value.complianceDigestHourUtc ?? 6)
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.message.set('Company profile saved.');
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to save company profile.'));
        }
      });
  }
}
