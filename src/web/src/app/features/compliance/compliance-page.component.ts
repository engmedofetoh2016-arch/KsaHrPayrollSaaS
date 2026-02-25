import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { catchError, forkJoin, of } from 'rxjs';
import { apiConfig } from '../../core/config/api.config';
import { I18nService } from '../../core/services/i18n.service';

type ComplianceAlertRow = {
  id: string;
  employeeName: string;
  documentType: string;
  expiryDate: string;
  daysLeft: number;
  severity: string;
  isResolved: boolean;
};

type ComplianceScoreHistoryRow = {
  snapshotDate: string;
  score: number;
  grade: string;
};

type ComplianceAlertExplanationRow = {
  provider: string;
  usedFallback: boolean;
  explanation: string;
  nextAction: string;
  targetWindowDays: number;
  generatedAtUtc: string;
};

type SaudizationSimulationResult = {
  targetPercent: number;
  plannedSaudiHires: number;
  plannedNonSaudiHires: number;
  current: {
    saudiEmployees: number;
    totalEmployees: number;
    saudizationPercent: number;
    additionalSaudiEmployeesNeeded: number;
    risk: string;
    band: string;
  };
  projected: {
    saudiEmployees: number;
    totalEmployees: number;
    saudizationPercent: number;
    additionalSaudiEmployeesNeeded: number;
    risk: string;
    band: string;
  };
  deltaPercent: number;
  improvesBand: boolean;
  improvesRisk: boolean;
};

type DigestLogRow = {
  id: string;
  retryOfDeliveryId?: string | null;
  recipientEmail: string;
  triggerType: string;
  frequency: string;
  status: string;
  simulated: boolean;
  errorMessage: string;
  sentAtUtc?: string | null;
  createdAtUtc: string;
};

@Component({
  selector: 'app-compliance-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './compliance-page.component.html',
  styleUrl: './compliance-page.component.scss'
})
export class CompliancePageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  readonly i18n = inject(I18nService);
  private readonly base = `${apiConfig.baseUrl}/api`;

  readonly loading = signal(false);
  readonly loadingSimulation = signal(false);
  readonly loadingDigestLogs = signal(false);
  readonly sendingDigest = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly resolvingIds = signal<string[]>([]);
  readonly loadingExplainIds = signal<string[]>([]);
  readonly selectedAlertIds = signal<string[]>([]);
  readonly alertExplanations = signal<Record<string, ComplianceAlertExplanationRow>>({});
  readonly score = signal({
    score: 0,
    grade: 'D',
    saudizationPercent: 0,
    saudizationTargetPercent: 30,
    saudizationGapPercent: 0,
    saudiEmployees: 0,
    totalEmployees: 0,
    additionalSaudiEmployeesNeeded: 0,
    wpsCompanyReady: false,
    employeesMissingPaymentData: 0,
    criticalAlerts: 0,
    warningAlerts: 0,
    noticeAlerts: 0,
    recommendations: [] as string[]
  });
  readonly aiBrief = signal({
    provider: 'fallback-disabled',
    usedFallback: true,
    brief: ''
  });
  readonly alerts = signal<ComplianceAlertRow[]>([]);
  readonly simulationInputs = signal({
    plannedSaudiHires: 1,
    plannedNonSaudiHires: 0
  });
  readonly simulation = signal<SaudizationSimulationResult | null>(null);
  readonly scoreHistory = signal<ComplianceScoreHistoryRow[]>([]);
  readonly digestLogs = signal<DigestLogRow[]>([]);
  readonly retryingDigestId = signal<string>('');
  readonly digestTotal = signal(0);
  readonly digestHasMore = signal(false);
  readonly digestSkip = signal(0);
  readonly digestTake = signal(50);
  readonly digestStatusFilter = signal('All');
  readonly digestTriggerFilter = signal('All');
  readonly digestSearch = signal('');
  readonly visibleDigestLogs = computed(() => this.digestLogs());

  ngOnInit(): void {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.message.set('');
    this.error.set('');

    forkJoin({
      score: this.http.get<any>(`${this.base}/compliance/score`).pipe(catchError(() => of(null))),
      brief: this.http
        .post<any>(`${this.base}/compliance/ai-brief`, {
          language: this.i18n.language(),
          prompt: this.i18n.text(
            'Summarize highest Saudi HR compliance risks and exact next actions.',
            'لخص أهم مخاطر الامتثال للموارد البشرية في السعودية مع الخطوات التالية بدقة.'
          )
        })
        .pipe(catchError(() => of(null))),
      simulation: this.http
        .get<any>(`${this.base}/compliance/saudization-simulation?plannedSaudiHires=1&plannedNonSaudiHires=0`)
        .pipe(catchError(() => of(null))),
      alerts: this.http.get<any>(`${this.base}/compliance/alerts?take=200`).pipe(catchError(() => of({ items: [] }))),
      history: this.http.get<any>(`${this.base}/compliance/score-history?days=60`).pipe(catchError(() => of({ items: [] })))
    }).subscribe({
      next: ({ score, brief, simulation, alerts, history }) => {
        if (score) {
          this.score.set({
            score: Number(score.score ?? 0),
            grade: String(score.grade ?? 'D'),
            saudizationPercent: Number(score.saudizationPercent ?? 0),
            saudizationTargetPercent: Number(score.saudizationTargetPercent ?? 30),
            saudizationGapPercent: Number(score.saudizationGapPercent ?? 0),
            saudiEmployees: Number(score.saudiEmployees ?? 0),
            totalEmployees: Number(score.totalEmployees ?? 0),
            additionalSaudiEmployeesNeeded: Number(score.additionalSaudiEmployeesNeeded ?? 0),
            wpsCompanyReady: !!score.wpsCompanyReady,
            employeesMissingPaymentData: Number(score.employeesMissingPaymentData ?? 0),
            criticalAlerts: Number(score.criticalAlerts ?? 0),
            warningAlerts: Number(score.warningAlerts ?? 0),
            noticeAlerts: Number(score.noticeAlerts ?? 0),
            recommendations: Array.isArray(score.recommendations) ? score.recommendations : []
          });
        }

        if (brief) {
          this.aiBrief.set({
            provider: String(brief.provider ?? 'fallback-disabled'),
            usedFallback: !!brief.usedFallback,
            brief: String(brief.brief ?? '')
          });
        }

        if (simulation) {
          this.simulation.set(this.mapSimulation(simulation));
        }

        const items = Array.isArray(alerts?.items) ? alerts.items : [];
        this.alerts.set(
          items.map((x: any) => ({
            id: String(x.id),
            employeeName: String(x.employeeName ?? ''),
            documentType: String(x.documentType ?? ''),
            expiryDate: String(x.expiryDate ?? ''),
            daysLeft: Number(x.daysLeft ?? 0),
            severity: String(x.severity ?? 'Notice'),
            isResolved: !!x.isResolved
          }))
        );
        this.selectedAlertIds.set([]);
        const currentIds = new Set(items.map((x: any) => String(x.id)));
        this.alertExplanations.update((existing) => {
          const next: Record<string, ComplianceAlertExplanationRow> = {};
          for (const id of Object.keys(existing)) {
            if (currentIds.has(id)) {
              next[id] = existing[id];
            }
          }
          return next;
        });

        const historyItems = Array.isArray(history?.items) ? history.items : [];
        this.scoreHistory.set(
          historyItems.map((x: any) => ({
            snapshotDate: String(x.snapshotDate ?? ''),
            score: Number(x.score ?? 0),
            grade: String(x.grade ?? 'D')
          }))
        );

        this.refreshDigestLogs(true);
      },
      error: () => {
        this.error.set(this.i18n.text('Failed to load compliance center.', 'تعذر تحميل مركز الامتثال.'));
      },
      complete: () => this.loading.set(false)
    });
  }

  resolveAlert(alertId: string) {
    if (!alertId || this.resolvingIds().includes(alertId)) {
      return;
    }

    this.resolvingIds.update((ids) => [...ids, alertId]);
    this.http.post(`${this.base}/compliance/alerts/${alertId}/resolve`, { reason: 'ResolvedFromComplianceCenter' }).subscribe({
      next: () => this.load(),
      error: () => {
        this.error.set(this.i18n.text('Failed to resolve alert.', 'تعذر إغلاق التنبيه.'));
      },
      complete: () => this.resolvingIds.update((ids) => ids.filter((id) => id !== alertId))
    });
  }

  explainAlert(alertId: string) {
    if (!alertId || this.loadingExplainIds().includes(alertId)) {
      return;
    }

    this.loadingExplainIds.update((ids) => [...ids, alertId]);
    this.http.get<any>(`${this.base}/compliance/alerts/${alertId}/explain-risk?language=${encodeURIComponent(this.i18n.language())}`).subscribe({
      next: (res) => {
        this.alertExplanations.update((existing) => ({
          ...existing,
          [alertId]: {
            provider: String(res?.provider ?? 'fallback-disabled'),
            usedFallback: !!res?.usedFallback,
            explanation: String(res?.explanation ?? ''),
            nextAction: String(res?.nextAction ?? ''),
            targetWindowDays: Number(res?.targetWindowDays ?? 30),
            generatedAtUtc: String(res?.generatedAtUtc ?? '')
          }
        }));
      },
      error: () => {
        this.error.set(this.i18n.text('Failed to explain risk.', 'تعذر شرح الخطر.'));
      },
      complete: () => this.loadingExplainIds.update((ids) => ids.filter((id) => id !== alertId))
    });
  }

  alertExplanation(alertId: string): ComplianceAlertExplanationRow | null {
    return this.alertExplanations()[alertId] ?? null;
  }

  documentLabel(documentType: string): string {
    if (documentType === 'Iqama') {
      return this.i18n.text('Iqama', 'الإقامة');
    }

    if (documentType === 'WorkPermit') {
      return this.i18n.text('Work Permit', 'رخصة العمل');
    }

    if (documentType === 'Contract') {
      return this.i18n.text('Contract', 'العقد');
    }

    return documentType;
  }

  runSaudizationSimulation() {
    const input = this.simulationInputs();
    const plannedSaudiHires = Math.max(0, Math.min(500, Number(input.plannedSaudiHires ?? 0)));
    const plannedNonSaudiHires = Math.max(0, Math.min(500, Number(input.plannedNonSaudiHires ?? 0)));

    this.loadingSimulation.set(true);
    this.http
      .get<any>(
        `${this.base}/compliance/saudization-simulation?plannedSaudiHires=${plannedSaudiHires}&plannedNonSaudiHires=${plannedNonSaudiHires}`
      )
      .subscribe({
        next: (res) => {
          this.simulation.set(this.mapSimulation(res));
        },
        error: () => {
          this.error.set(this.i18n.text('Failed to run Saudization simulation.', 'تعذر تشغيل محاكاة التوطين.'));
        },
        complete: () => this.loadingSimulation.set(false)
      });
  }

  updatePlannedSaudiHires(value: string) {
    const numeric = Number(value);
    this.simulationInputs.update((x) => ({
      ...x,
      plannedSaudiHires: Number.isFinite(numeric) ? Math.max(0, Math.min(500, Math.floor(numeric))) : 0
    }));
  }

  updatePlannedNonSaudiHires(value: string) {
    const numeric = Number(value);
    this.simulationInputs.update((x) => ({
      ...x,
      plannedNonSaudiHires: Number.isFinite(numeric) ? Math.max(0, Math.min(500, Math.floor(numeric))) : 0
    }));
  }

  bandClass(band: string): string {
    const normalized = (band ?? '').toLowerCase();
    if (normalized === 'green') {
      return 'band-green';
    }

    if (normalized === 'yellow') {
      return 'band-yellow';
    }

    return 'band-red';
  }

  severityLabel(severity: string): string {
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

    return severity;
  }

  bandLabel(band: string): string {
    const normalized = (band ?? '').toLowerCase();
    if (normalized === 'green') {
      return this.i18n.text('Green', 'أخضر');
    }

    if (normalized === 'yellow') {
      return this.i18n.text('Yellow', 'أصفر');
    }

    if (normalized === 'red') {
      return this.i18n.text('Red', 'أحمر');
    }

    return band;
  }

  isSelected(alertId: string): boolean {
    return this.selectedAlertIds().includes(alertId);
  }

  toggleSelection(alertId: string, checked: boolean) {
    if (!alertId) {
      return;
    }

    if (checked) {
      this.selectedAlertIds.update((ids) => (ids.includes(alertId) ? ids : [...ids, alertId]));
      return;
    }

    this.selectedAlertIds.update((ids) => ids.filter((id) => id !== alertId));
  }

  toggleSelectAll(checked: boolean) {
    if (!checked) {
      this.selectedAlertIds.set([]);
      return;
    }

    this.selectedAlertIds.set(this.alerts().map((x) => x.id));
  }

  resolveSelected() {
    const ids = this.selectedAlertIds();
    if (ids.length === 0) {
      return;
    }

    this.loading.set(true);
    this.http
      .post(`${this.base}/compliance/alerts/resolve-bulk`, {
        alertIds: ids,
        reason: 'ResolvedFromComplianceCenterBulk'
      })
      .subscribe({
        next: () => this.load(),
        error: () => {
          this.loading.set(false);
          this.error.set(this.i18n.text('Failed to resolve selected alerts.', 'تعذر إغلاق التنبيهات المحددة.'));
        }
      });
  }

  exportCsv() {
    const score = this.score();
    const alerts = this.alerts();
    const history = this.scoreHistory();
    const lines: string[] = [];

    lines.push('Section,Field,Value');
    lines.push(`Score,Compliance Score,${score.score}`);
    lines.push(`Score,Grade,${this.escapeCsv(score.grade)}`);
    lines.push(`Score,Saudization Percent,${score.saudizationPercent}`);
    lines.push(`Score,Saudization Target Percent,${score.saudizationTargetPercent}`);
    lines.push(`Score,Saudization Gap Percent,${score.saudizationGapPercent}`);
    lines.push(`Score,Saudi Employees,${score.saudiEmployees}`);
    lines.push(`Score,Total Employees,${score.totalEmployees}`);
    lines.push(`Score,Additional Saudi Employees Needed,${score.additionalSaudiEmployeesNeeded}`);
    lines.push(`Score,WPS Company Ready,${score.wpsCompanyReady}`);
    lines.push(`Score,Employees Missing Payment Data,${score.employeesMissingPaymentData}`);
    lines.push(`Score,Critical Alerts,${score.criticalAlerts}`);
    lines.push(`Score,Warning Alerts,${score.warningAlerts}`);
    lines.push(`Score,Notice Alerts,${score.noticeAlerts}`);

    for (const recommendation of score.recommendations) {
      lines.push(`Recommendation,Item,${this.escapeCsv(recommendation)}`);
    }

    lines.push('');
    lines.push('Trend,SnapshotDate,Score,Grade');
    for (const point of history) {
      lines.push(`Trend,${this.escapeCsv(point.snapshotDate)},${point.score},${this.escapeCsv(point.grade)}`);
    }

    lines.push('');
    lines.push('Alerts,Employee,Document,ExpiryDate,DaysLeft,Severity');
    for (const alert of alerts) {
      lines.push(
        [
          'Alert',
          this.escapeCsv(alert.employeeName),
          this.escapeCsv(alert.documentType),
          this.escapeCsv(alert.expiryDate),
          alert.daysLeft.toString(),
          this.escapeCsv(alert.severity)
        ].join(',')
      );
    }

    const csv = lines.join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const datePart = new Date().toISOString().slice(0, 10);
    const fileName = `compliance-center-${datePart}.csv`;
    this.downloadBlob(blob, fileName);
  }

  exportPdf() {
    window.print();
  }

  sendDigestNow() {
    if (this.sendingDigest()) {
      return;
    }

    this.sendingDigest.set(true);
    this.error.set('');
    this.message.set('');

    this.http.post<any>(`${this.base}/compliance/digest/send-now`, {}).subscribe({
      next: (res) => {
        const to = String(res?.to ?? '');
        const simulated = !!res?.simulated;
        this.message.set(
          simulated
            ? this.i18n.text(
                `Digest simulated${to ? ` for ${to}` : ''} (SMTP not configured).`,
                to ? `تمت محاكاة الإرسال إلى ${to} (SMTP غير مهيأ).` : 'تمت محاكاة الإرسال (SMTP غير مهيأ).'
              )
            : this.i18n.text(
                `Compliance digest sent${to ? ` to ${to}` : ''}.`,
                to ? `تم إرسال ملخص الامتثال إلى ${to}.` : 'تم إرسال ملخص الامتثال.'
              )
        );
      },
      error: () => {
        this.error.set(this.i18n.text('Failed to send compliance digest.', 'تعذر إرسال ملخص الامتثال.'));
      },
      complete: () => {
        this.sendingDigest.set(false);
        this.refreshDigestLogs(true);
      }
    });
  }

  retryDigest(deliveryId: string) {
    if (!deliveryId || this.retryingDigestId()) {
      return;
    }

    this.retryingDigestId.set(deliveryId);
    this.error.set('');
    this.message.set('');

    this.http.post<any>(`${this.base}/compliance/digest/retry/${deliveryId}`, {}).subscribe({
      next: (res) => {
        const simulated = !!res?.simulated;
        this.message.set(
          simulated
            ? this.i18n.text('Digest retry simulated (SMTP not configured).', 'تمت محاكاة إعادة الإرسال (SMTP غير مهيأ).')
            : this.i18n.text('Digest retry sent successfully.', 'تم إرسال إعادة الملخص بنجاح.')
        );
        this.load();
      },
      error: () => {
        this.error.set(this.i18n.text('Failed to retry digest.', 'تعذر إعادة إرسال الملخص.'));
      },
      complete: () => this.retryingDigestId.set('')
    });
  }

  setDigestStatusFilter(value: string) {
    this.digestStatusFilter.set(value || 'All');
    this.refreshDigestLogs(true);
  }

  setDigestTriggerFilter(value: string) {
    this.digestTriggerFilter.set(value || 'All');
    this.refreshDigestLogs(true);
  }

  setDigestSearch(value: string) {
    this.digestSearch.set(value ?? '');
    this.refreshDigestLogs(true);
  }

  loadMoreDigestLogs() {
    if (!this.digestHasMore() || this.loadingDigestLogs()) {
      return;
    }

    this.refreshDigestLogs(false);
  }

  severityClass(severity: string): string {
    const normalized = severity.toLowerCase();
    if (normalized === 'critical') {
      return 'sev-critical';
    }

    if (normalized === 'warning') {
      return 'sev-warning';
    }

    return 'sev-notice';
  }

  historyPointHeight(score: number): string {
    const clamped = Math.max(0, Math.min(100, score));
    return `${Math.max(8, clamped)}%`;
  }

  canRetryDigest(status: string): boolean {
    return status.toLowerCase() === 'failed';
  }

  exportDigestLogsCsv() {
    const lines: string[] = [];
    lines.push('CreatedAtUtc,RecipientEmail,TriggerType,Frequency,Status,Simulated,ErrorMessage,SentAtUtc');

    for (const log of this.digestLogs()) {
      lines.push(
        [
          this.escapeCsv(log.createdAtUtc),
          this.escapeCsv(log.recipientEmail),
          this.escapeCsv(log.triggerType),
          this.escapeCsv(log.frequency),
          this.escapeCsv(log.status),
          log.simulated ? 'true' : 'false',
          this.escapeCsv(log.errorMessage ?? ''),
          this.escapeCsv(log.sentAtUtc ?? '')
        ].join(',')
      );
    }

    const csv = lines.join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const datePart = new Date().toISOString().slice(0, 10);
    this.downloadBlob(blob, `digest-delivery-log-${datePart}.csv`);
  }

  private refreshDigestLogs(reset: boolean) {
    const skip = reset ? 0 : this.digestSkip();
    const take = this.digestTake();
    this.loadingDigestLogs.set(true);

    this.http.get<any>(`${this.base}/compliance/digest/logs${this.buildDigestLogsQuery(skip, take)}`).pipe(catchError(() => of({ items: [] }))).subscribe({
      next: (digestLogs) => {
        const logItems = Array.isArray(digestLogs?.items) ? digestLogs.items : [];
        const mapped: DigestLogRow[] = logItems.map((x: any) => ({
          id: String(x.id),
          retryOfDeliveryId: x.retryOfDeliveryId ? String(x.retryOfDeliveryId) : null,
          recipientEmail: String(x.recipientEmail ?? ''),
          triggerType: String(x.triggerType ?? ''),
          frequency: String(x.frequency ?? ''),
          status: String(x.status ?? ''),
          simulated: !!x.simulated,
          errorMessage: String(x.errorMessage ?? ''),
          sentAtUtc: x.sentAtUtc ? String(x.sentAtUtc) : null,
          createdAtUtc: String(x.createdAtUtc ?? '')
        }));

        this.digestLogs.set(reset ? mapped : [...this.digestLogs(), ...mapped]);
        this.digestTotal.set(Number(digestLogs?.total ?? this.digestLogs().length));
        this.digestSkip.set(skip + mapped.length);
        this.digestHasMore.set(!!digestLogs?.hasMore);
      },
      error: () => {
        this.error.set(this.i18n.text('Failed to load digest log entries.', 'ØªØ¹Ø°Ø± ØªØ­Ù…ÙŠÙ„ Ø³Ø¬Ù„ Ø¥Ø±Ø³Ø§Ù„ Ø§Ù„Ù…Ù„Ø®Øµ.'));
      },
      complete: () => this.loadingDigestLogs.set(false)
    });
  }

  private buildDigestLogsQuery(skip: number, take: number): string {
    const parts: string[] = [];
    parts.push(`skip=${skip}`);
    parts.push(`take=${take}`);

    const status = this.digestStatusFilter();
    const trigger = this.digestTriggerFilter();
    const search = this.digestSearch().trim();

    if (status && status !== 'All') {
      parts.push(`status=${encodeURIComponent(status)}`);
    }

    if (trigger && trigger !== 'All') {
      parts.push(`triggerType=${encodeURIComponent(trigger)}`);
    }

    if (search) {
      parts.push(`search=${encodeURIComponent(search)}`);
    }

    return `?${parts.join('&')}`;
  }

  private escapeCsv(value: string): string {
    const v = value ?? '';
    if (v.includes(',') || v.includes('"') || v.includes('\n') || v.includes('\r')) {
      return `"${v.replace(/"/g, '""')}"`;
    }

    return v;
  }

  private mapSimulation(raw: any): SaudizationSimulationResult {
    return {
      targetPercent: Number(raw?.targetPercent ?? 30),
      plannedSaudiHires: Number(raw?.plannedSaudiHires ?? 0),
      plannedNonSaudiHires: Number(raw?.plannedNonSaudiHires ?? 0),
      current: {
        saudiEmployees: Number(raw?.current?.saudiEmployees ?? 0),
        totalEmployees: Number(raw?.current?.totalEmployees ?? 0),
        saudizationPercent: Number(raw?.current?.saudizationPercent ?? 0),
        additionalSaudiEmployeesNeeded: Number(raw?.current?.additionalSaudiEmployeesNeeded ?? 0),
        risk: String(raw?.current?.risk ?? 'High'),
        band: String(raw?.current?.band ?? 'Red')
      },
      projected: {
        saudiEmployees: Number(raw?.projected?.saudiEmployees ?? 0),
        totalEmployees: Number(raw?.projected?.totalEmployees ?? 0),
        saudizationPercent: Number(raw?.projected?.saudizationPercent ?? 0),
        additionalSaudiEmployeesNeeded: Number(raw?.projected?.additionalSaudiEmployeesNeeded ?? 0),
        risk: String(raw?.projected?.risk ?? 'High'),
        band: String(raw?.projected?.band ?? 'Red')
      },
      deltaPercent: Number(raw?.deltaPercent ?? 0),
      improvesBand: !!raw?.improvesBand,
      improvesRisk: !!raw?.improvesRisk
    };
  }

  private downloadBlob(blob: Blob, fileName: string) {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
