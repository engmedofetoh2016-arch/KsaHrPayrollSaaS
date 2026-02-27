import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';

@Component({
  selector: 'app-shell-layout',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell-layout.component.html',
  styleUrl: './shell-layout.component.scss'
})
export class ShellLayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly i18n = inject(I18nService);

  readonly session = this.auth.session;
  readonly isEmployee = computed(() => this.auth.hasAnyRole(['Employee']));
  readonly canAccessCompliance = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR', 'Manager']));
  readonly canAccessCompany = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR']));
  readonly canAccessEmployees = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR']));
  readonly canAccessFinalSettlement = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR']));
  readonly canAccessLoans = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR']));
  readonly canAccessOffboarding = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR', 'Manager']));
  readonly canAccessAttendance = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR', 'Manager', 'Employee']));
  readonly canAccessPayroll = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR']));
  readonly canAccessSmartAlerts = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR', 'Manager']));
  readonly canManageUsers = computed(() => this.auth.hasAnyRole(['Owner', 'Admin']));
  readonly canApproveLeave = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR', 'Manager']));
  readonly canAccessGovernanceRegistry = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR', 'Manager']));
  readonly canAccessEssReview = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR', 'Manager']));
  isMobileNavOpen = false;

  logout() {
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }

  toggleMobileNav() {
    this.isMobileNavOpen = !this.isMobileNavOpen;
  }

  closeMobileNav() {
    this.isMobileNavOpen = false;
  }
}
