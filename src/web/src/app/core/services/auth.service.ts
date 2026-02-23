import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { map, tap } from 'rxjs';
import { apiConfig } from '../config/api.config';
import {
  AuthSession,
  ChangePasswordRequest,
  ForgotPasswordRequest,
  LoginRequest,
  LoginResponse,
  ResetPasswordRequest,
  SignUpRequest
} from '../models/auth.models';

const STORAGE_KEY = 'hrpayroll.session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly sessionSignal = signal<AuthSession | null>(this.loadSession());

  readonly session = computed(() => this.sessionSignal());
  readonly isAuthenticated = computed(() => this.sessionSignal() !== null);
  readonly roles = computed(() => this.sessionSignal()?.roles ?? []);

  login(request: LoginRequest) {
    return this.http
      .post<LoginResponse>(`${apiConfig.baseUrl}/api/auth/login`, request)
      .pipe(
        map((response) => ({
          accessToken: response.accessToken,
          tenantId: response.user.tenantId,
          roles: response.user.roles,
          email: response.user.email,
          firstName: response.user.firstName,
          lastName: response.user.lastName,
          mustChangePassword: Boolean(response.mustChangePassword)
        })),
        tap((session) => this.setSession(session))
      );
  }

  signUp(request: SignUpRequest) {
    return this.http.post(`${apiConfig.baseUrl}/api/tenants`, request);
  }

  forgotPassword(request: ForgotPasswordRequest) {
    return this.http.post<{ message: string }>(`${apiConfig.baseUrl}/api/auth/forgot-password`, request);
  }

  resetPassword(request: ResetPasswordRequest) {
    return this.http.post<{ message: string }>(`${apiConfig.baseUrl}/api/auth/reset-password`, request);
  }

  changePassword(request: ChangePasswordRequest) {
    return this.http.post<{ message: string }>(`${apiConfig.baseUrl}/api/auth/change-password`, request).pipe(
      tap(() => {
        const session = this.sessionSignal();
        if (!session) {
          return;
        }
        this.setSession({ ...session, mustChangePassword: false });
      })
    );
  }

  logout() {
    localStorage.removeItem(STORAGE_KEY);
    this.sessionSignal.set(null);
  }

  hasAnyRole(requiredRoles: string[]): boolean {
    if (requiredRoles.length === 0) {
      return true;
    }

    const currentRoles = this.roles();
    return requiredRoles.some((role) => currentRoles.includes(role));
  }

  getToken(): string | null {
    return this.sessionSignal()?.accessToken ?? null;
  }

  getTenantId(): string | null {
    return this.sessionSignal()?.tenantId ?? null;
  }

  private setSession(session: AuthSession) {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    this.sessionSignal.set(session);
  }

  private loadSession(): AuthSession | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }

    try {
      const parsed = JSON.parse(raw) as Partial<AuthSession>;
      return {
        accessToken: parsed.accessToken ?? '',
        tenantId: parsed.tenantId ?? '',
        roles: parsed.roles ?? [],
        email: parsed.email ?? '',
        firstName: parsed.firstName ?? '',
        lastName: parsed.lastName ?? '',
        mustChangePassword: Boolean(parsed.mustChangePassword)
      };
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  }
}
