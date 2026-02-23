import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    return router.createUrlTree(['/login']);
  }

  const session = authService.session();
  const mustChange = Boolean(session?.mustChangePassword);
  const isChangePasswordRoute = state.url.startsWith('/change-password');

  if (mustChange && !isChangePasswordRoute) {
    return router.createUrlTree(['/change-password']);
  }

  return true;
};
