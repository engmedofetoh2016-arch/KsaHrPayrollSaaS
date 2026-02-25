import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { SignUpRequest } from '../../core/models/auth.models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss'
})
export class LoginPageComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly i18n = inject(I18nService);

  readonly loading = signal(false);
  readonly signupLoading = signal(false);
  readonly error = signal('');
  readonly signupError = signal('');
  readonly signupMessage = signal('');
  readonly showSignUp = signal(false);
  readonly forgotLoading = signal(false);
  readonly forgotMessage = signal('');
  readonly forgotError = signal('');
  readonly showForgot = signal(false);

  readonly form = this.fb.group({
    tenantSlug: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]]
  });

  readonly signupForm = this.fb.group({
    tenantName: ['', [Validators.required, Validators.maxLength(120)]],
    slug: ['', [Validators.required, Validators.maxLength(120), Validators.pattern('^[a-zA-Z0-9-]+$')]],
    companyLegalName: ['', [Validators.required, Validators.maxLength(200)]],
    currencyCode: ['SAR', [Validators.required, Validators.minLength(3), Validators.maxLength(3)]],
    defaultPayDay: [25, [Validators.required, Validators.min(1), Validators.max(31)]],
    ownerFirstName: ['', [Validators.required, Validators.maxLength(80)]],
    ownerLastName: ['', [Validators.required, Validators.maxLength(80)]],
    ownerEmail: ['', [Validators.required, Validators.email]],
    ownerPassword: ['', [Validators.required, Validators.minLength(8)]]
  });

  readonly forgotForm = this.fb.group({
    tenantSlug: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]]
  });

  constructor() {
    const reason = this.route.snapshot.queryParamMap.get('reason');
    if (reason === 'expired') {
      this.error.set(this.i18n.text('Session expired. Please login again.', 'انتهت صلاحية الجلسة. يرجى تسجيل الدخول مرة أخرى.'));
    }
  }

  onSubmit() {
    if (this.form.invalid || this.loading()) {
      this.form.markAllAsTouched();
      if (this.form.invalid) {
        this.error.set(this.i18n.text('Please enter tenant slug, valid email, and password (min 8 chars).', 'يرجى إدخال رمز المنشأة وبريد إلكتروني صحيح وكلمة مرور لا تقل عن 8 أحرف.'));
      }
      return;
    }

    this.loading.set(true);
    this.error.set('');

    this.authService.login(this.form.getRawValue() as { tenantSlug: string; email: string; password: string }).subscribe({
      next: (session) => {
        this.loading.set(false);
        if (session.mustChangePassword) {
          this.router.navigateByUrl('/change-password');
          return;
        }
        this.router.navigateByUrl('/dashboard');
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, this.i18n.text('Login failed. Check tenant slug, email, and password.', 'تعذر تسجيل الدخول. يرجى التحقق من رمز المنشأة والبريد الإلكتروني وكلمة المرور.')));
      }
    });
  }

  toggleSignUp() {
    const next = !this.showSignUp();
    this.showSignUp.set(next);
    if (next) {
      this.showForgot.set(false);
    }
    this.signupError.set('');
    this.signupMessage.set('');
  }

  toggleForgotPassword() {
    const next = !this.showForgot();
    this.showForgot.set(next);
    if (next) {
      this.showSignUp.set(false);
    }
    this.forgotError.set('');
    this.forgotMessage.set('');
    if (this.showForgot()) {
      this.forgotForm.patchValue({
        tenantSlug: this.form.getRawValue().tenantSlug ?? '',
        email: this.form.getRawValue().email ?? ''
      });
    }
  }

  showLoginPanel() {
    this.showSignUp.set(false);
    this.showForgot.set(false);
    this.error.set('');
    this.signupError.set('');
    this.forgotError.set('');
  }

  onForgotSubmit() {
    if (this.forgotForm.invalid || this.forgotLoading()) {
      this.forgotForm.markAllAsTouched();
      if (this.forgotForm.invalid) {
        this.forgotError.set(this.i18n.text('Please enter tenant slug and valid email.', 'يرجى إدخال رمز المنشأة وبريد إلكتروني صحيح.'));
      }
      return;
    }

    this.forgotLoading.set(true);
    this.forgotError.set('');
    this.forgotMessage.set('');
    const payload = this.forgotForm.getRawValue() as { tenantSlug: string; email: string };
    this.authService.forgotPassword(payload).subscribe({
      next: () => {
        this.forgotLoading.set(false);
        this.forgotMessage.set(this.i18n.text('If account exists, reset instructions were sent.', 'في حال وجود الحساب، تم إرسال تعليمات إعادة تعيين كلمة المرور.'));
      },
      error: (err) => {
        this.forgotLoading.set(false);
        this.forgotError.set(getApiErrorMessage(err, this.i18n.text('Failed to submit forgot password request.', 'تعذر إرسال طلب استعادة كلمة المرور.')));
      }
    });
  }

  onSignUpSubmit() {
    if (this.signupForm.invalid || this.signupLoading()) {
      this.signupForm.markAllAsTouched();
      if (this.signupForm.invalid) {
        this.signupError.set(this.i18n.text('Please complete all required sign-up fields correctly.', 'يرجى استكمال جميع حقول التسجيل المطلوبة بشكل صحيح.'));
      }
      return;
    }

    this.signupLoading.set(true);
    this.signupError.set('');
    this.signupMessage.set('');

    const raw = this.signupForm.getRawValue();
    const payload: SignUpRequest = {
      tenantName: String(raw.tenantName ?? '').trim(),
      slug: String(raw.slug ?? '').trim().toLowerCase(),
      companyLegalName: String(raw.companyLegalName ?? '').trim(),
      currencyCode: String(raw.currencyCode ?? '').trim().toUpperCase(),
      defaultPayDay: Number(raw.defaultPayDay),
      ownerFirstName: String(raw.ownerFirstName ?? '').trim(),
      ownerLastName: String(raw.ownerLastName ?? '').trim(),
      ownerEmail: String(raw.ownerEmail ?? '').trim(),
      ownerPassword: String(raw.ownerPassword ?? '')
    };

    this.authService.signUp(payload).subscribe({
      next: () => {
        this.signupLoading.set(false);
        this.signupMessage.set(this.i18n.text('Sign up successful. You can login now.', 'تم تسجيل المنشأة بنجاح. يمكنك تسجيل الدخول الآن.'));
        this.form.patchValue({
          tenantSlug: String(payload.slug),
          email: String(payload.ownerEmail),
          password: String(payload.ownerPassword)
        });
        this.showSignUp.set(false);
      },
      error: (err) => {
        this.signupLoading.set(false);
        this.signupError.set(getApiErrorMessage(err, this.i18n.text('Sign up failed. Please review your details.', 'تعذر تسجيل المنشأة. يرجى مراجعة البيانات المدخلة.')));
      }
    });
  }
}
