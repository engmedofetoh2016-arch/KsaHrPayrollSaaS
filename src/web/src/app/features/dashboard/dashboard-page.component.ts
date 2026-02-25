import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { apiConfig } from '../../core/config/api.config';
import { MyPayslipItem } from '../../core/models/me.models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { MeService } from '../../core/services/me.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

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
  private readonly meService = inject(MeService);
  readonly i18n = inject(I18nService);
  private readonly base = `${apiConfig.baseUrl}/api`;

  readonly loading = signal(true);
  readonly error = signal('');
  readonly session = this.auth.session;
  readonly today = new Date();
  readonly canSeeUsers = computed(() => this.auth.hasAnyRole(['Owner', 'Admin']));
  readonly isEmployee = computed(() => this.auth.hasAnyRole(['Employee']));
  readonly canSeeGovernance = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR', 'Manager']));
  readonly canSeePayrollIntelligence = computed(() => this.auth.hasAnyRole(['Owner', 'Admin', 'HR']));
  readonly latestPayslip = signal<MyPayslipItem | null>(null);
  readonly latestPayslipBusy = signal(false);

  readonly stats = signal({
    employees: 0,
    saudiEmployees: 0,
    expiringIqama: 0,
    expiringWorkPermit: 0,
    expiringContract: 0,
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
  readonly payrollGovernance = signal({
    windowDays: 30,
    totalApprovals: 0,
    standardApprovals: 0,
    overrideApprovals: 0,
    overrideRatePercent: 0,
    overridesWithReference: 0,
    overrideReferenceCoveragePercent: 0,
    overrideDocumentationQualityPercent: 0,
    criticalDecisionCount: 0,
    topCriticalCodes: [] as Array<{ code: string; count: number }>,
    topOverrideCategories: [] as Array<{ category: string; count: number }>
  });
  readonly payrollGovernanceTrend = signal<Array<{
    month: string;
    totalApprovals: number;
    overrideApprovals: number;
    overrideRatePercent: number;
  }>>([]);
  readonly payrollIntelligenceLoading = signal(false);
  readonly payrollIntelligenceError = signal('');
  readonly payrollIntelligence = signal({
    runsScanned: 0,
    runsWithBlocking: 0,
    criticalFindings: 0,
    warningFindings: 0,
    noticeFindings: 0,
    items: [] as Array<{
      runId: string;
      hasBlockingFindings: boolean;
      critical: number;
      warning: number;
      notice: number;
      topFinding: string;
      generatedAtUtc: string;
    }>
  });
  readonly governanceDecisionDrilldown = signal({
    loading: false,
    selectedCode: '',
      items: [] as Array<{
        id: string;
        createdAtUtc: string;
        decisionType: 'Standard' | 'Override' | string;
        runId?: string;
        category?: string;
        referenceId?: string;
        criticalCodes?: string;
        warningCount?: string;
        reason?: string;
      }>
  });
  readonly governanceRisk = computed(() => {
    const rate = this.payrollGovernance().overrideRatePercent;
    if (rate >= 15) {
      return 'high';
    }
    if (rate >= 5) {
      return 'medium';
    }
    return 'low';
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
      latestPayslip: this.isEmployee() ? this.meService.getLatestPayslip().pipe(catchError(() => of(null))) : of(null),
      governance: this.canSeeGovernance()
        ? this.http.get<any>(`${this.base}/payroll/governance/overview?days=30`).pipe(catchError(() => of(null)))
        : of(null),
      governanceTrend: this.canSeeGovernance()
        ? this.http.get<any>(`${this.base}/payroll/governance/trend?months=6`).pipe(catchError(() => of(null)))
        : of(null),
      intelligenceSeed: this.canSeePayrollIntelligence()
        ? this.http.get<any>(`${this.base}/payroll/governance/decisions?days=45&take=120`).pipe(catchError(() => of(null)))
        : of(null),
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
      next: ({ employeesPage, attendancePage, payrollPeriods, usersPage, companyProfile, employeePaymentPage, compliance, complianceScore, latestPayslip, governance, governanceTrend, intelligenceSeed, complianceBrief }) => {
        this.stats.set({
          employees: this.extractCount(employeesPage),
          saudiEmployees: this.extractItems(employeePaymentPage).filter((x) => !!x.isSaudiNational).length,
          expiringIqama: this.extractItems(employeePaymentPage).filter((x) => this.isExpiringWithin(x.iqamaExpiryDate, 60)).length,
          expiringWorkPermit: this.extractItems(employeePaymentPage).filter((x) => this.isExpiringWithin(x.workPermitExpiryDate, 60)).length,
          expiringContract: this.extractItems(employeePaymentPage).filter((x) => this.isExpiringWithin(x.contractEndDate, 60)).length,
          expiringDocuments: this.extractItems(employeePaymentPage).filter(
            (x) =>
              this.isExpiringWithin(x.iqamaExpiryDate, 60) ||
              this.isExpiringWithin(x.workPermitExpiryDate, 60) ||
              this.isExpiringWithin(x.contractEndDate, 60)
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

        if (governance) {
          this.payrollGovernance.set({
            windowDays: Number(governance?.windowDays ?? 30),
            totalApprovals: Number(governance?.totalApprovals ?? 0),
            standardApprovals: Number(governance?.standardApprovals ?? 0),
            overrideApprovals: Number(governance?.overrideApprovals ?? 0),
            overrideRatePercent: Number(governance?.overrideRatePercent ?? 0),
            overridesWithReference: Number(governance?.overridesWithReference ?? 0),
            overrideReferenceCoveragePercent: Number(governance?.overrideReferenceCoveragePercent ?? 0),
            overrideDocumentationQualityPercent: Number(governance?.overrideDocumentationQualityPercent ?? 0),
            criticalDecisionCount: Number(governance?.criticalDecisionCount ?? 0),
            topCriticalCodes: Array.isArray(governance?.topCriticalCodes)
              ? governance.topCriticalCodes.map((x: any) => ({
                  code: String(x?.code ?? ''),
                  count: Number(x?.count ?? 0)
                }))
              : [],
            topOverrideCategories: Array.isArray(governance?.topOverrideCategories)
              ? governance.topOverrideCategories.map((x: any) => ({
                  category: String(x?.category ?? ''),
                  count: Number(x?.count ?? 0)
                }))
              : []
          });
        }

        if (governanceTrend && Array.isArray(governanceTrend?.items)) {
          this.payrollGovernanceTrend.set(
            governanceTrend.items.map((x: any) => ({
              month: String(x?.month ?? ''),
              totalApprovals: Number(x?.totalApprovals ?? 0),
              overrideApprovals: Number(x?.overrideApprovals ?? 0),
              overrideRatePercent: Number(x?.overrideRatePercent ?? 0)
            }))
          );
        } else {
          this.payrollGovernanceTrend.set([]);
        }

        this.loadPayrollIntelligence(intelligenceSeed);
        this.latestPayslip.set(latestPayslip);
      },
      complete: () => this.loading.set(false),
      error: () => {
        this.loading.set(false);
        this.error.set(this.i18n.text('Failed to load dashboard metrics.', 'تعذر تحميل مؤشرات لوحة التحكم.'));
      }
    });
  }

  complianceSeverityLabel(severity?: string | null): string {
    const normalized = (severity ?? '').toLowerCase();
    if (normalized === 'critical') {
      return this.i18n.text('Critical', 'حرج');
    }

    if (normalized === 'warning') {
      return this.i18n.text('Warning', 'متوسط');
    }

    if (normalized === 'notice') {
      return this.i18n.text('Notice', 'تنبيه');
    }

    return severity ?? '-';
  }

  complianceProviderLabel(): string {
    const provider = String(this.complianceBrief().provider ?? '').trim().toLowerCase();
    if (!provider) {
      return this.i18n.text('Unknown', 'غير معروف');
    }

    if (provider === 'fallback-disabled') {
      return this.i18n.text('Fallback (AI disabled)', 'وضع بديل (الذكاء الاصطناعي غير مفعل)');
    }

    if (provider === 'claude') {
      return this.i18n.text('Claude AI', 'كلود للذكاء الاصطناعي');
    }

    return provider;
  }

  displayRecommendation(text: string): string {
    const value = String(text ?? '').trim();
    if (!value || !this.i18n.isArabic()) {
      return value;
    }

    const saudizationMatch = value.match(/Raise\s+Saudization\s+ratio\s+to\s+at\s+least\s+(\d+)%\s+target\s*\(\s*need\s+(\d+)\s+additional\s+Saudi\s+employee\(s\)\s+at\s+current\s+headcount\s*\)\.?/i);
    if (saudizationMatch) {
      const target = saudizationMatch[1];
      const needed = saudizationMatch[2];
      return `رفع نسبة التوطين إلى ${target}% على الأقل (يتطلب ${needed} موظف سعودي إضافي حسب العدد الحالي).`;
    }

    const wpsMatch = value.match(/Complete payment profiles for (\d+) employees\./i);
    if (wpsMatch) {
      return `استكمال بيانات الدفع لعدد ${wpsMatch[1]} من الموظفين.`;
    }

    const criticalMatch = value.match(/Resolve (\d+) critical compliance alerts within 7 days\./i);
    if (criticalMatch) {
      return `إغلاق ${criticalMatch[1]} من تنبيهات الامتثال الحرجة خلال 7 أيام.`;
    }

    const warningMatch = value.match(/Plan remediation for (\d+) warning alerts within 30 days\./i);
    if (warningMatch) {
      return `وضع خطة معالجة لعدد ${warningMatch[1]} من التنبيهات المتوسطة خلال 30 يومًا.`;
    }

    if (/Complete company WPS bank profile\./i.test(value)) {
      return 'استكمال ملف حساب الشركة البنكي الخاص بنظام حماية الأجور (WPS).';
    }

    if (/Maintain current controls and run weekly compliance review\./i.test(value)) {
      return 'الاستمرار على الضوابط الحالية وإجراء مراجعة امتثال أسبوعية.';
    }

    if (/Raise Saudization ratio/i.test(value) && /additional Saudi employee/i.test(value)) {
      const target = value.match(/(\d+)%/)?.[1] ?? '30';
      const needed = value.match(/need\s+(\d+)/i)?.[1] ?? '1';
      return `رفع نسبة التوطين إلى ${target}% على الأقل (يتطلب ${needed} موظف سعودي إضافي حسب العدد الحالي).`;
    }

    return value;
  }

  formatComplianceBrief(text: string): string {
    const value = String(text ?? '').trim();
    if (!value || !this.i18n.isArabic()) {
      return value;
    }

    return value
      .replace(/\(fallback-disabled\)/gi, '(وضع بديل)')
      .replace(/^- /gm, '• ')
      .replace(/\n-\s/g, '\n• ');
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

  private loadPayrollIntelligence(seed: any) {
    if (!this.canSeePayrollIntelligence()) {
      this.payrollIntelligence.set({
        runsScanned: 0,
        runsWithBlocking: 0,
        criticalFindings: 0,
        warningFindings: 0,
        noticeFindings: 0,
        items: []
      });
      this.payrollIntelligenceError.set('');
      this.payrollIntelligenceLoading.set(false);
      return;
    }

    const runIds = Array.from(
      new Set(
        (Array.isArray(seed?.items) ? seed.items : [])
          .map((x: any) => String(x?.runId ?? '').trim())
          .filter((x: string) => this.looksLikeGuid(x))
      )
    ).slice(0, 10);

    if (runIds.length === 0) {
      this.payrollIntelligence.set({
        runsScanned: 0,
        runsWithBlocking: 0,
        criticalFindings: 0,
        warningFindings: 0,
        noticeFindings: 0,
        items: []
      });
      this.payrollIntelligenceError.set('');
      this.payrollIntelligenceLoading.set(false);
      return;
    }

    this.payrollIntelligenceLoading.set(true);
    this.payrollIntelligenceError.set('');

    forkJoin(
      runIds.map((runId) =>
        this.http.get<any>(`${this.base}/payroll/runs/${runId}/pre-approval-checks`).pipe(
          catchError(() =>
            of({
              runId,
              hasBlockingFindings: false,
              generatedAtUtc: '',
              findings: []
            })
          )
        )
      )
    ).subscribe({
      next: (results) => {
        const rows = results.map((result: any) => {
          const findings = Array.isArray(result?.findings) ? result.findings : [];
          const critical = findings.filter((x: any) => String(x?.severity ?? '') === 'Critical');
          const warning = findings.filter((x: any) => String(x?.severity ?? '') === 'Warning');
          const notice = findings.filter((x: any) => String(x?.severity ?? '') === 'Notice');
          const topFinding = critical[0]?.message ?? warning[0]?.message ?? notice[0]?.message ?? '';
          return {
            runId: String(result?.runId ?? ''),
            hasBlockingFindings: !!result?.hasBlockingFindings,
            critical: critical.length,
            warning: warning.length,
            notice: notice.length,
            topFinding: String(topFinding ?? ''),
            generatedAtUtc: String(result?.generatedAtUtc ?? '')
          };
        });

        const sortedRows = rows
          .filter((x) => this.looksLikeGuid(x.runId))
          .sort((a, b) => {
            if (a.hasBlockingFindings !== b.hasBlockingFindings) {
              return a.hasBlockingFindings ? -1 : 1;
            }

            if (a.critical !== b.critical) {
              return b.critical - a.critical;
            }

            return b.warning - a.warning;
          });

        this.payrollIntelligence.set({
          runsScanned: sortedRows.length,
          runsWithBlocking: sortedRows.filter((x) => x.hasBlockingFindings).length,
          criticalFindings: sortedRows.reduce((sum, row) => sum + row.critical, 0),
          warningFindings: sortedRows.reduce((sum, row) => sum + row.warning, 0),
          noticeFindings: sortedRows.reduce((sum, row) => sum + row.notice, 0),
          items: sortedRows
        });
      },
      error: () => {
        this.payrollIntelligenceError.set(this.i18n.text('Failed to load payroll intelligence.', 'تعذر تحميل ذكاء الرواتب.'));
      },
      complete: () => this.payrollIntelligenceLoading.set(false)
    });
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

  governanceBarHeight(rate: number): string {
    const clamped = Math.max(0, Math.min(100, Number(rate) || 0));
    return `${Math.max(6, clamped)}%`;
  }

  loadGovernanceDecisionsByCode(code: string) {
    const normalizedCode = String(code ?? '').trim();
    if (!normalizedCode) {
      return;
    }

    this.governanceDecisionDrilldown.set({
      loading: true,
      selectedCode: normalizedCode,
      items: []
    });

    const url = `${this.base}/payroll/governance/decisions?days=30&criticalCode=${encodeURIComponent(normalizedCode)}&take=100`;
    this.http.get<any>(url).pipe(catchError(() => of({ items: [] }))).subscribe({
      next: (response) => {
        const items = Array.isArray(response?.items) ? response.items : [];
        this.governanceDecisionDrilldown.set({
          loading: false,
          selectedCode: normalizedCode,
          items: items.map((x: any) => ({
            id: String(x?.id ?? ''),
            createdAtUtc: String(x?.createdAtUtc ?? ''),
            decisionType: String(x?.decisionType ?? ''),
            runId: x?.runId ? String(x.runId) : undefined,
            category: x?.category ? String(x.category) : undefined,
            referenceId: x?.referenceId ? String(x.referenceId) : undefined,
            criticalCodes: x?.criticalCodes ? String(x.criticalCodes) : undefined,
            warningCount: x?.warningCount ? String(x.warningCount) : undefined,
            reason: x?.reason ? String(x.reason) : undefined
          }))
        });
      },
      error: () => {
        this.governanceDecisionDrilldown.set({
          loading: false,
          selectedCode: normalizedCode,
          items: []
        });
      }
    });
  }

  clearGovernanceDrilldown() {
    this.governanceDecisionDrilldown.set({
      loading: false,
      selectedCode: '',
      items: []
    });
  }

  private looksLikeGuid(value: string): boolean {
    return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
  }

  downloadLatestPayslip() {
    const payslip = this.latestPayslip();
    if (!payslip || payslip.status !== 3 || this.latestPayslipBusy()) {
      return;
    }

    this.latestPayslipBusy.set(true);
    this.error.set('');

    this.meService.downloadPayslip(payslip.id).subscribe({
      next: (blob) => {
        this.latestPayslipBusy.set(false);
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = payslip.fileName || `payslip-${payslip.periodYear}-${String(payslip.periodMonth).padStart(2, '0')}.pdf`;
        link.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => {
        this.latestPayslipBusy.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to download latest payslip.'));
      }
    });
  }
}
