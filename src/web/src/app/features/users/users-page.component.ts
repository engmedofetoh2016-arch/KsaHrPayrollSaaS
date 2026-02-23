import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AppUser } from '../../core/models/user.models';
import { I18nService } from '../../core/services/i18n.service';
import { UsersService } from '../../core/services/users.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-users-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './users-page.component.html',
  styleUrl: './users-page.component.scss'
})
export class UsersPageComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly usersService = inject(UsersService);
  readonly i18n = inject(I18nService);

  readonly users = signal<AppUser[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly unlockingUserId = signal<string | null>(null);
  readonly togglingUserId = signal<string | null>(null);
  readonly resettingPasswordUserId = signal<string | null>(null);
  readonly message = signal('');
  readonly error = signal('');

  readonly roles = ['Owner', 'Admin', 'HR', 'Manager', 'Employee'];

  readonly form = this.fb.group({
    firstName: ['', [Validators.required, Validators.minLength(2)]],
    lastName: ['', [Validators.required, Validators.minLength(2)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    role: ['HR', [Validators.required]]
  });

  ngOnInit(): void {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');

    this.usersService.list().subscribe({
      next: (users) => {
        this.users.set(users);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load users.'));
      }
    });
  }

  create() {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.message.set('');
    this.error.set('');

    const value = this.form.getRawValue();
    this.usersService
      .create({
        firstName: value.firstName ?? '',
        lastName: value.lastName ?? '',
        email: value.email ?? '',
        password: value.password ?? '',
        role: value.role ?? 'HR'
      })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.message.set('User created.');
          this.form.patchValue({ password: '' });
          this.load();
        },
        error: (err) => {
          this.saving.set(false);
          this.error.set(getApiErrorMessage(err, 'Failed to create user.'));
        }
      });
  }

  isLocked(user: AppUser): boolean {
    if (this.isDisabled(user)) {
      return false;
    }

    if (!user.lockoutEnd) {
      return false;
    }
    return new Date(user.lockoutEnd).getTime() > Date.now();
  }

  isDisabled(user: AppUser): boolean {
    if (!user.lockoutEnd) {
      return false;
    }
    const yearsFromNow = Date.now() + (1000 * 60 * 60 * 24 * 365 * 50);
    return new Date(user.lockoutEnd).getTime() > yearsFromNow;
  }

  unlock(user: AppUser) {
    if (this.unlockingUserId()) {
      return;
    }

    this.unlockingUserId.set(user.id);
    this.message.set('');
    this.error.set('');

    this.usersService.unlock(user.id).subscribe({
      next: () => {
        this.unlockingUserId.set(null);
        this.message.set(`User ${user.email} unlocked.`);
        this.load();
      },
      error: (err) => {
        this.unlockingUserId.set(null);
        this.error.set(getApiErrorMessage(err, 'Failed to unlock user.'));
      }
    });
  }

  disable(user: AppUser) {
    if (this.togglingUserId()) {
      return;
    }

    const confirmed = window.confirm(`Disable ${user.email}?`);
    if (!confirmed) {
      return;
    }

    this.togglingUserId.set(user.id);
    this.message.set('');
    this.error.set('');

    this.usersService.disable(user.id).subscribe({
      next: () => {
        this.togglingUserId.set(null);
        this.message.set(`User ${user.email} disabled.`);
        this.load();
      },
      error: (err) => {
        this.togglingUserId.set(null);
        this.error.set(getApiErrorMessage(err, 'Failed to disable user.'));
      }
    });
  }

  enable(user: AppUser) {
    if (this.togglingUserId()) {
      return;
    }

    this.togglingUserId.set(user.id);
    this.message.set('');
    this.error.set('');

    this.usersService.enable(user.id).subscribe({
      next: () => {
        this.togglingUserId.set(null);
        this.message.set(`User ${user.email} enabled.`);
        this.load();
      },
      error: (err) => {
        this.togglingUserId.set(null);
        this.error.set(getApiErrorMessage(err, 'Failed to enable user.'));
      }
    });
  }

  adminResetPassword(user: AppUser) {
    if (this.resettingPasswordUserId()) {
      return;
    }

    const newPassword = window.prompt(`Set new password for ${user.email} (min 8 chars, uppercase, lowercase, number):`);
    if (newPassword === null) {
      return;
    }

    const confirmPassword = window.prompt('Confirm new password:');
    if (confirmPassword === null) {
      return;
    }

    if (newPassword !== confirmPassword) {
      this.error.set('Passwords do not match.');
      return;
    }

    this.resettingPasswordUserId.set(user.id);
    this.message.set('');
    this.error.set('');

    this.usersService.adminResetPassword(user.id, newPassword).subscribe({
      next: () => {
        this.resettingPasswordUserId.set(null);
        this.message.set(`Password reset for ${user.email}. User must change it on next login.`);
      },
      error: (err) => {
        this.resettingPasswordUserId.set(null);
        this.error.set(getApiErrorMessage(err, 'Failed to reset user password.'));
      }
    });
  }
}
