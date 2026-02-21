import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { apiConfig } from '../../core/config/api.config';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss'
})
export class DashboardPageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  readonly i18n = inject(I18nService);
  private readonly base = `${apiConfig.baseUrl}/api`;

  readonly loading = signal(true);
  readonly error = signal('');
  readonly session = this.auth.session;
  readonly today = new Date();
  readonly canSeeUsers = computed(() => this.auth.hasAnyRole(['Owner', 'Admin']));

  readonly stats = signal({
    employees: 0,
    saudiEmployees: 0,
    expiringIqama: 0,
    expiringWorkPermit: 0,
    expiringDocuments: 0,
    attendanceRowsThisMonth: 0,
    payrollPeriods: 0,
    users: 0
  });
  readonly wpsReadiness = signal({
    companyReady: false,
    employeesMissingPayment: 0,
    totalEmployees: 0,
    ready: false
  });
  readonly complianceAlerts = signal({
    critical: 0,
    warning: 0,
    notice: 0,
    items: [] as Array<{
      id: string;
      employeeName: string;
      documentType: string;
      expiryDate: string;
      daysLeft: number;
      severity?: string;
    }>
  });
  readonly resolvingAlertIds = signal<string[]>([]);
  readonly complianceScore = signal({
    score: 0,
    grade: 'D',
    saudizationPercent: 0,
    wpsCompanyReady: false,
    criticalAlerts: 0,
    warningAlerts: 0,
    noticeAlerts: 0,
    recommendations: [] as string[]
  });
  readonly complianceBrief = signal({
    provider: 'fallback-disabled',
    usedFallback: true,
    brief: ''
  });

  readonly saudization = computed(() => {
    const total = this.stats().employees;
    const saudi = this.stats().saudiEmployees;
    const percent = total <= 0 ? 0 : Math.round((saudi / total) * 1000) / 10;
    const risk = percent >= 30 ? 'low' : percent >= 20 ? 'medium' : 'high';
    return { total, saudi, percent, risk };
  });

  ngOnInit(): void {
    this.loadStats();
  }

  private loadStats() {
    this.loading.set(true);
    this.error.set('');

    forkJoin({
      employeesPage: this.http.get<any>(`${this.base}/employees?page=1&pageSize=1`).pipe(catchError(() => of({ items: [], total: 0 }))),
      attendancePage: this.http
        .get<any>(`${this.base}/attendance-inputs?year=${this.today.getFullYear()}&month=${this.today.getMonth() + 1}&page=1&pageSize=1`)
        .pipe(catchError(() => of({ items: [], total: 0 }))),
      payrollPeriods: this.http.get<any[]>(`${this.base}/payroll/periods`).pipe(catchError(() => of([]))),
      usersPage: this.canSeeUsers()
        ? this.http.get<any>(`${this.base}/users?page=1&pageSize=1`).pipe(catchError(() => of({ items: [], total: 0 })))
        : of({ items: [], total: 0 }),
      companyProfile: this.http.get<any>(`${this.base}/company-profile`).pipe(catchError(() => of(null))),
      employeePaymentPage: this.http.get<any>(`${this.base}/employees?page=1&pageSize=500`).pipe(catchError(() => of({ items: [], total: 0 }))),
      compliance: this.http.get<any>(`${this.base}/compliance/alerts?take=8`).pipe(catchError(() => of({ critical: 0, warning: 0, notice: 0, items: [] }))),
      complianceScore: this.http.get<any>(`${this.base}/compliance/score`).pipe(catchError(() => of(null))),
      complianceBrief: this.http
        .post<any>(`${this.base}/compliance/ai-brief`, {
          language: this.i18n.language(),
          prompt: this.i18n.text(
            'Focus on concrete HR actions for this week, this month, and next 60 days.',
            'ركز على إجراءات الموارد البشرية لهذا الأسبوع وهذا الشهر وخلال 60 يوما.'
          )
        })
        .pipe(catchError(() => of(null)))
    }).subscribe({
      next: ({ employeesPage, attendancePage, payrollPeriods, usersPage, companyProfile, employeePaymentPage, compliance, complianceScore, complianceBrief }) => {
        this.stats.set({
          employees: this.extractCount(employeesPage),
          saudiEmployees: this.extractItems(employeePaymentPage).filter((x) => !!x.isSaudiNational).length,
          expiringIqama: this.extractItems(employeePaymentPage).filter((x) => this.isExpiringWithin(x.iqamaExpiryDate, 60)).length,
          expiringWorkPermit: this.extractItems(employeePaymentPage).filter((x) => this.isExpiringWithin(x.workPermitExpiryDate, 60)).length,
          expiringDocuments: this.extractItems(employeePaymentPage).filter(
            (x) => this.isExpiringWithin(x.iqamaExpiryDate, 60) || this.isExpiringWithin(x.workPermitExpiryDate, 60)
          ).length,
          attendanceRowsThisMonth: this.extractCount(attendancePage),
          payrollPeriods: Array.isArray(payrollPeriods) ? payrollPeriods.length : 0,
          users: this.extractCount(usersPage)
        });

        const companyReady =
          !!companyProfile &&
          this.hasValue(companyProfile.wpsCompanyBankName) &&
          this.hasValue(companyProfile.wpsCompanyBankCode) &&
          this.hasValue(companyProfile.wpsCompanyIban);

        const employeeItems = this.extractItems(employeePaymentPage);
        const totalEmployees = typeof employeePaymentPage?.total === 'number' ? employeePaymentPage.total : employeeItems.length;
        const employeesMissingPayment = employeeItems.filter(
          (employee) => !this.hasValue(employee.employeeNumber) || !this.hasValue(employee.bankName) || !this.hasValue(employee.bankIban)
        ).length;

        this.wpsReadiness.set({
          companyReady,
          employeesMissingPayment,
          totalEmployees,
          ready: companyReady && totalEmployees > 0 && employeesMissingPayment === 0
        });

        this.complianceAlerts.set({
          critical: Number(compliance?.critical ?? 0),
          warning: Number(compliance?.warning ?? 0),
          notice: Number(compliance?.notice ?? 0),
          items: Array.isArray(compliance?.items) ? compliance.items : []
        });

        if (complianceScore) {
          this.complianceScore.set({
            score: Number(complianceScore?.score ?? 0),
            grade: String(complianceScore?.grade ?? 'D'),
            saudizationPercent: Number(complianceScore?.saudizationPercent ?? 0),
            wpsCompanyReady: !!complianceScore?.wpsCompanyReady,
            criticalAlerts: Number(complianceScore?.criticalAlerts ?? 0),
            warningAlerts: Number(complianceScore?.warningAlerts ?? 0),
            noticeAlerts: Number(complianceScore?.noticeAlerts ?? 0),
            recommendations: Array.isArray(complianceScore?.recommendations) ? complianceScore.recommendations : []
          });
        }

        if (complianceBrief) {
          this.complianceBrief.set({
            provider: String(complianceBrief?.provider ?? 'fallback-disabled'),
            usedFallback: !!complianceBrief?.usedFallback,
            brief: String(complianceBrief?.brief ?? '')
          });
        }
      },
      complete: () => this.loading.set(false),
      error: () => {
        this.loading.set(false);
        this.error.set(this.i18n.text('Failed to load dashboard metrics.', 'تعذر تحميل مؤشرات لوحة التحكم.'));
      }
    });
  }

  private extractCount(response: any): number {
    if (Array.isArray(response)) {
      return response.length;
    }

    if (response && Array.isArray(response.items)) {
      return typeof response.total === 'number' ? response.total : response.items.length;
    }

    return 0;
  }

  private extractItems(response: any): any[] {
    if (Array.isArray(response)) {
      return response;
    }

    if (response && Array.isArray(response.items)) {
      return response.items;
    }

    return [];
  }

  private hasValue(value: unknown): boolean {
    return typeof value === 'string' ? value.trim().length > 0 : value !== null && value !== undefined;
  }

  private isExpiringWithin(dateText: unknown, withinDays: number): boolean {
    if (typeof dateText !== 'string' || !dateText.trim()) {
      return false;
    }

    const target = new Date(`${dateText}T00:00:00`);
    if (Number.isNaN(target.getTime())) {
      return false;
    }

    const now = new Date();
    now.setHours(0, 0, 0, 0);
    const msPerDay = 24 * 60 * 60 * 1000;
    const days = Math.floor((target.getTime() - now.getTime()) / msPerDay);
    return days >= 0 && days <= withinDays;
  }

  resolveAlert(alertId: string) {
    if (!alertId || this.resolvingAlertIds().includes(alertId)) {
      return;
    }

    this.resolvingAlertIds.update((ids) => [...ids, alertId]);
    this.http.post(`${this.base}/compliance/alerts/${alertId}/resolve`, { reason: 'ResolvedFromDashboard' }).subscribe({
      next: () => this.loadStats(),
      error: () => {},
      complete: () => this.resolvingAlertIds.update((ids) => ids.filter((id) => id !== alertId))
    });
  }
}
