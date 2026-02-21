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
}
