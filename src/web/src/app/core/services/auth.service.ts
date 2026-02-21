import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { map, tap } from 'rxjs';
import { apiConfig } from '../config/api.config';
import { AuthSession, LoginRequest, LoginResponse, SignUpRequest } from '../models/auth.models';

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
          lastName: response.user.lastName
        })),
        tap((session) => this.setSession(session))
      );
  }

  signUp(request: SignUpRequest) {
    return this.http.post(`${apiConfig.baseUrl}/api/tenants`, request);
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
      return JSON.parse(raw) as AuthSession;
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  }
}
