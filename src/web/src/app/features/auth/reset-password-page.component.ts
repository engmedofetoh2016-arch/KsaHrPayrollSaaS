import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-reset-password-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './reset-password-page.component.html',
  styleUrl: './reset-password-page.component.scss'
})
export class ResetPasswordPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly i18n = inject(I18nService);

  readonly loading = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  readonly form = this.fb.group({
    tenantSlug: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    token: ['', [Validators.required]],
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', [Validators.required, Validators.minLength(8)]]
  });

  constructor() {
    const qp = this.route.snapshot.queryParamMap;
    const token = qp.get('token') ?? '';
    const tenantSlug = qp.get('tenantSlug') ?? '';
    const email = qp.get('email') ?? '';
    this.form.patchValue({
      token,
      tenantSlug,
      email
    });
  }

  submit() {
    if (this.form.invalid || this.loading()) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    if (value.newPassword !== value.confirmPassword) {
      this.error.set('New password and confirm password do not match.');
      return;
    }

    this.loading.set(true);
    this.error.set('');
    this.message.set('');

    this.authService
      .resetPassword({
        tenantSlug: value.tenantSlug ?? '',
        email: value.email ?? '',
        token: value.token ?? '',
        newPassword: value.newPassword ?? ''
      })
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.message.set('Password reset successful. Redirecting to login...');
          setTimeout(() => this.router.navigateByUrl('/login'), 1000);
        },
        error: (err) => {
          this.loading.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to reset password.'));
        }
      });
  }
}

