import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { catchError, forkJoin, of } from 'rxjs';
import { apiConfig } from '../../core/config/api.config';
import { I18nService } from '../../core/services/i18n.service';

@Component({
  selector: 'app-operations-studio-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './operations-studio-page.component.html',
  styleUrl: './operations-studio-page.component.scss'
})
export class OperationsStudioPageComponent implements OnInit {
  private readonly http = inject(HttpClient);
  readonly i18n = inject(I18nService);
  private readonly base = `${apiConfig.baseUrl}/api`;

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly showTechnicalCodes = signal(false);
  readonly error = signal('');
  readonly message = signal('');

  readonly allowanceMatrix = signal<any[]>([]);
  readonly approvalMatrix = signal<any[]>([]);
  readonly complianceRules = signal<any[]>([]);
  readonly forecastScenarios = signal<any[]>([]);
  readonly notificationTemplates = signal<any[]>([]);
  readonly notificationQueue = signal<any[]>([]);
  readonly integrationJobs = signal<any[]>([]);
  readonly integrationDashboard = signal<any>({
    total: 0,
    queued: 0,
    processing: 0,
    retryScheduled: 0,
    deadLetter: 0,
    succeeded: 0,
    deadlinesMissed: 0,
    recentFailures: []
  });
  readonly integrationStatusFilter = signal('All');
  readonly filteredIntegrationJobs = computed(() => {
    const items = this.integrationJobs();
    const filter = this.integrationStatusFilter().trim().toLowerCase();
    if (!filter || filter === 'all') {
      return items;
    }

    return items.filter((x) => String(x?.status ?? '').trim().toLowerCase() === filter);
  });
  readonly escalationNotifications = signal<any[]>([]);
  readonly escalationStatusFilter = signal('All');
  readonly filteredEscalationNotifications = computed(() => {
    const filter = this.escalationStatusFilter().trim().toLowerCase();
    const items = this.escalationNotifications();
    if (!filter || filter === 'all') {
      return items;
    }

    return items.filter((x) => String(x?.status ?? '').trim().toLowerCase() === filter);
  });
  readonly dataQualityIssues = signal<any[]>([]);
  readonly selfServiceRequests = signal<any[]>([]);

  readonly allowanceForm = {
    policyName: 'قياسي',
    gradeCode: 'G1',
    locationCode: 'RIYADH',
    housingAmount: 1000,
    transportAmount: 500,
    mealAmount: 300,
    prorationMethod: 'CalendarDays',
    effectiveFrom: new Date().toISOString().slice(0, 10),
    effectiveTo: '',
    isTaxable: false,
    isActive: true
  };

  readonly stageForm = {
    payrollScope: 'Default',
    stageCode: 'REVIEWER',
    stageName: 'مراجع',
    stageOrder: 1,
    approverRole: 'Manager',
    allowRollback: true,
    autoApproveEnabled: false,
    slaEscalationHours: 24,
    escalationRole: 'Owner',
    isActive: true
  };

  readonly ruleForm = {
    ruleCode: 'WPS_MISSING',
    ruleName: 'نقص بيانات حماية الأجور',
    ruleCategory: 'WPS',
    severity: 'Critical',
    ruleConfigJson: '{"daysThreshold":3}',
    isEnabled: true
  };

  readonly forecastForm = {
    scenarioName: 'سيناريو شهري',
    basePayrollRunId: '',
    plannedSaudiHires: 1,
    plannedNonSaudiHires: 0,
    plannedAttrition: 0,
    plannedSalaryDeltaPercent: 0,
    assumptionsJson: '{}'
  };

  readonly templateForm = {
    templateCode: 'CONTRACT_EXPIRY',
    channel: 'Email',
    subject: 'انتهاء العقد قريب',
    body: 'سينتهي عقدك قريبًا. يرجى المراجعة.',
    isActive: true
  };

  readonly queueForm = {
    recipientType: 'Employee',
    recipientValue: '',
    channel: 'Email',
    templateCode: 'CONTRACT_EXPIRY',
    relatedEntityType: 'Contract',
    relatedEntityId: '',
    payloadJson: '{}'
  };

  readonly integrationForm = {
    provider: 'Qiwa',
    operation: 'EmployeeProfileSync',
    entityType: 'Employee',
    entityId: '',
    deadlineAtUtc: '',
    requestPayloadJson: '{}'
  };

  ngOnInit(): void {
    this.refresh();
  }

  toggleTechnicalCodes() {
    this.showTechnicalCodes.set(!this.showTechnicalCodes());
  }

  refresh() {
    this.loading.set(true);
    this.error.set('');
    this.message.set('');

    forkJoin({
      allowanceMatrix: this.http.get<any>(`${this.base}/payroll/allowance-policy-matrix?activeOnly=true`).pipe(catchError(() => of([]))),
      approvalMatrix: this.http.get<any>(`${this.base}/payroll/approval-matrix?payrollScope=Default`).pipe(catchError(() => of([]))),
      complianceRules: this.http.get<any>(`${this.base}/compliance/rules?enabledOnly=true`).pipe(catchError(() => of({ items: [] }))),
      forecastScenarios: this.http.get<any>(`${this.base}/analytics/payroll-forecast/scenarios`).pipe(catchError(() => of({ items: [] }))),
      selfServiceRequests: this.http.get<any>(`${this.base}/self-service/requests?status=Submitted&take=100`).pipe(catchError(() => of({ items: [] }))),
      notificationTemplates: this.http.get<any>(`${this.base}/notifications/templates`).pipe(catchError(() => of({ items: [] }))),
      notificationQueue: this.http.get<any>(`${this.base}/notifications/queue?take=200`).pipe(catchError(() => of({ items: [] }))),
      integrationJobs: this.http.get<any>(`${this.base}/integrations/sync-jobs?take=200`).pipe(catchError(() => of({ items: [] }))),
      integrationDashboard: this.http.get<any>(`${this.base}/integrations/sync-jobs/dashboard`).pipe(catchError(() => of({
        total: 0,
        queued: 0,
        processing: 0,
        retryScheduled: 0,
        deadLetter: 0,
        succeeded: 0,
        deadlinesMissed: 0,
        recentFailures: []
      }))),
      dataQualityIssues: this.http.get<any>(`${this.base}/payroll/data-quality/issues?status=Open&take=100`).pipe(catchError(() => of({ items: [] })))
    }).subscribe({
      next: (response) => {
        this.allowanceMatrix.set(Array.isArray(response.allowanceMatrix) ? response.allowanceMatrix : []);
        this.approvalMatrix.set(Array.isArray(response.approvalMatrix) ? response.approvalMatrix : []);
        this.complianceRules.set(Array.isArray(response.complianceRules?.items) ? response.complianceRules.items : []);
        this.forecastScenarios.set(Array.isArray(response.forecastScenarios?.items) ? response.forecastScenarios.items : []);
        this.selfServiceRequests.set(Array.isArray(response.selfServiceRequests?.items) ? response.selfServiceRequests.items : []);
        this.notificationTemplates.set(Array.isArray(response.notificationTemplates?.items) ? response.notificationTemplates.items : []);
        const queueItems = Array.isArray(response.notificationQueue?.items) ? response.notificationQueue.items : [];
        this.notificationQueue.set(queueItems);
        this.integrationJobs.set(Array.isArray(response.integrationJobs?.items) ? response.integrationJobs.items : []);
        this.integrationDashboard.set(response.integrationDashboard ?? {
          total: 0,
          queued: 0,
          processing: 0,
          retryScheduled: 0,
          deadLetter: 0,
          succeeded: 0,
          deadlinesMissed: 0,
          recentFailures: []
        });
        this.escalationNotifications.set(
          queueItems
            .filter((x: any) => String(x?.templateCode ?? '').toUpperCase() === 'PAYROLL_SLA_ESCALATION')
            .map((x: any) => {
              const payload = this.parseEscalationPayload(x?.payloadJson);
              return {
                id: String(x?.id ?? ''),
                status: String(x?.status ?? ''),
                channel: String(x?.channel ?? ''),
                recipientType: String(x?.recipientType ?? ''),
                recipientValue: String(x?.recipientValue ?? ''),
                scheduledAtUtc: String(x?.scheduledAtUtc ?? ''),
                sentAtUtc: x?.sentAtUtc ? String(x.sentAtUtc) : '',
                errorMessage: String(x?.errorMessage ?? ''),
                relatedEntityId: x?.relatedEntityId ? String(x.relatedEntityId) : '',
                runId: payload.runId,
                stageCode: payload.stageCode,
                stageName: payload.stageName,
                escalatedToRole: payload.escalatedToRole,
                period: payload.period
              };
            })
        );
        this.dataQualityIssues.set(Array.isArray(response.dataQualityIssues?.items) ? response.dataQualityIssues.items : []);
      },
      error: () => this.error.set(this.i18n.text('Failed to load operations studio.', 'تعذر تحميل استوديو العمليات.')),
      complete: () => this.loading.set(false)
    });
  }

  saveAllowanceMatrix() {
    this.runSave(
      this.http.post(`${this.base}/payroll/allowance-policy-matrix`, {
        policyName: this.allowanceForm.policyName,
        gradeCode: this.allowanceForm.gradeCode,
        locationCode: this.allowanceForm.locationCode,
        housingAmount: Number(this.allowanceForm.housingAmount || 0),
        transportAmount: Number(this.allowanceForm.transportAmount || 0),
        mealAmount: Number(this.allowanceForm.mealAmount || 0),
        prorationMethod: this.allowanceForm.prorationMethod,
        effectiveFrom: this.allowanceForm.effectiveFrom,
        effectiveTo: this.allowanceForm.effectiveTo || null,
        isTaxable: !!this.allowanceForm.isTaxable,
        isActive: !!this.allowanceForm.isActive
      }),
      this.i18n.text('Allowance matrix saved.', 'تم حفظ مصفوفة البدلات.')
    );
  }

  seedApprovalMatrix() {
    this.runSave(
      this.http.post(`${this.base}/payroll/approval-matrix/seed-default`, {}),
      this.i18n.text('Default approval matrix seeded.', 'تم إنشاء مصفوفة الاعتماد الافتراضية.')
    );
  }

  saveApprovalStage() {
    this.runSave(
      this.http.post(`${this.base}/payroll/approval-matrix`, {
        payrollScope: this.stageForm.payrollScope,
        stageCode: this.stageForm.stageCode,
        stageName: this.stageForm.stageName,
        stageOrder: Number(this.stageForm.stageOrder || 0),
        approverRole: this.stageForm.approverRole,
        allowRollback: !!this.stageForm.allowRollback,
        autoApproveEnabled: !!this.stageForm.autoApproveEnabled,
        slaEscalationHours: Number(this.stageForm.slaEscalationHours || 0),
        escalationRole: this.stageForm.escalationRole || null,
        isActive: !!this.stageForm.isActive
      }),
      this.i18n.text('Approval stage saved.', 'تم حفظ مرحلة الاعتماد.')
    );
  }

  saveComplianceRule() {
    this.runSave(
      this.http.post(`${this.base}/compliance/rules`, {
        ruleCode: this.ruleForm.ruleCode,
        ruleName: this.ruleForm.ruleName,
        ruleCategory: this.ruleForm.ruleCategory,
        severity: this.ruleForm.severity,
        ruleConfigJson: this.ruleForm.ruleConfigJson,
        isEnabled: !!this.ruleForm.isEnabled
      }),
      this.i18n.text('Compliance rule saved.', 'تم حفظ قاعدة الامتثال.')
    );
  }

  createForecastScenario() {
    this.runSave(
      this.http.post(`${this.base}/analytics/payroll-forecast/scenarios`, {
        scenarioName: this.forecastForm.scenarioName,
        basePayrollRunId: this.forecastForm.basePayrollRunId || null,
        plannedSaudiHires: Number(this.forecastForm.plannedSaudiHires || 0),
        plannedNonSaudiHires: Number(this.forecastForm.plannedNonSaudiHires || 0),
        plannedAttrition: Number(this.forecastForm.plannedAttrition || 0),
        plannedSalaryDeltaPercent: Number(this.forecastForm.plannedSalaryDeltaPercent || 0),
        assumptionsJson: this.forecastForm.assumptionsJson || '{}'
      }),
      this.i18n.text('Forecast scenario created.', 'تم إنشاء سيناريو التنبؤ.')
    );
  }

  runForecastScenario(scenarioId: string) {
    this.runSave(
      this.http.post(`${this.base}/analytics/payroll-forecast/scenarios/${scenarioId}/run`, {}),
      this.i18n.text('Forecast run completed.', 'تم تنفيذ التنبؤ.')
    );
  }

  saveNotificationTemplate() {
    this.runSave(
      this.http.post(`${this.base}/notifications/templates`, {
        templateCode: this.templateForm.templateCode,
        channel: 'Email',
        subject: this.templateForm.subject,
        body: this.templateForm.body,
        isActive: !!this.templateForm.isActive
      }),
      this.i18n.text('Notification template saved.', 'تم حفظ قالب الإشعار.')
    );
  }

  enqueueNotification() {
    this.runSave(
      this.http.post(`${this.base}/notifications/queue`, {
        recipientType: this.queueForm.recipientType,
        recipientValue: this.queueForm.recipientValue,
        channel: 'Email',
        templateCode: this.queueForm.templateCode,
        relatedEntityType: this.queueForm.relatedEntityType,
        relatedEntityId: this.queueForm.relatedEntityId || null,
        payloadJson: this.queueForm.payloadJson || '{}',
        scheduledAtUtc: null
      }),
      this.i18n.text('Notification queued.', 'تمت إضافة الإشعار إلى قائمة الانتظار.')
    );
  }

  queueIntegrationSyncJob() {
    const entityIdRaw = this.integrationForm.entityId.trim();
    const deadlineRaw = this.integrationForm.deadlineAtUtc.trim();
    this.runSave(
      this.http.post(`${this.base}/integrations/sync-jobs`, {
        provider: this.integrationForm.provider,
        operation: this.integrationForm.operation,
        entityType: this.integrationForm.entityType,
        entityId: entityIdRaw || null,
        requestPayloadJson: this.integrationForm.requestPayloadJson || '{}',
        deadlineAtUtc: deadlineRaw || null,
        maxAttempts: 3
      }),
      this.i18n.text('Integration sync job queued.', 'تمت إضافة مهمة مزامنة التكامل إلى قائمة الانتظار.')
    );
  }

  retryIntegrationJob(jobId: string) {
    if (!jobId) {
      return;
    }

    this.runSave(
      this.http.post(`${this.base}/integrations/sync-jobs/${jobId}/retry`, {}),
      this.i18n.text('Integration sync job re-queued.', 'تمت إعادة جدولة مهمة التكامل.')
    );
  }

  reviewSelfServiceRequest(requestId: string, approved: boolean) {
    if (!requestId) {
      return;
    }

    this.runSave(
      this.http.post(`${this.base}/self-service/requests/${requestId}/review`, {
        approved,
        resolutionNotes: approved ? 'Approved from Operations Studio.' : 'Rejected from Operations Studio.'
      }),
      approved
        ? this.i18n.text('Request approved and applied.', 'تمت الموافقة على الطلب وتطبيقه.')
        : this.i18n.text('Request rejected.', 'تم رفض الطلب.')
    );
  }

  runDataQualityScan() {
    this.runSave(
      this.http.post(`${this.base}/payroll/data-quality/scan`, {}),
      this.i18n.text('Data quality scan completed.', 'اكتمل فحص جودة البيانات.')
    );
  }

  fixOpenIssues() {
    const issueIds = this.dataQualityIssues()
      .filter((x) => x.fixActionCode !== 'ManualUpdateRequired')
      .map((x) => x.id);
    if (issueIds.length === 0) {
      this.error.set(this.i18n.text('No auto-fixable open issues found.', 'لا توجد مشكلات مفتوحة قابلة للإصلاح التلقائي.'));
      return;
    }

    this.runSave(
      this.http.post(`${this.base}/payroll/data-quality/fix-batch`, {
        issueIds,
        batchReference: `UI-${new Date().toISOString().replace(/[-:.TZ]/g, '').slice(0, 14)}`
      }),
      this.i18n.text('Fix batch applied.', 'تم تطبيق دفعة الإصلاح.')
    );
  }

  codeLabel(value: string | null | undefined): string {
    const code = String(value ?? '').trim().toUpperCase();
    switch (code) {
      case 'REVIEWER':
        return 'مرحلة المراجع';
      case 'FINANCE':
        return 'مرحلة المالية';
      case 'FINAL':
        return 'الاعتماد النهائي';
      case 'WPS_MISSING':
        return 'نقص بيانات حماية الأجور';
      case 'GOSI_MISSING':
        return 'نقص بيانات التأمينات الاجتماعية';
      case 'SAUDIZATION_GAP':
        return 'فجوة نسبة السعودة';
      case 'CONTRACT_EXPIRY':
        return 'تنبيه انتهاء العقد';
      case 'PROFILEUPDATE':
        return 'تحديث الملف الشخصي';
      case 'LEAVEREQUEST':
        return 'طلب إجازة';
      case 'LOANREQUEST':
        return 'طلب قرض';
      case 'CONTRACTRENEWAL':
        return 'تجديد عقد';
      case 'INTEGRATION_SYNC_FAILED':
        return 'فشل مزامنة التكامل';
      case 'INTEGRATION_DEADLINE_MISSED':
        return 'تجاوز موعد مزامنة التكامل';
      default:
        return code ? 'رمز مخصص' : 'غير محدد';
    }
  }

  channelLabel(value: string | null | undefined): string {
    const channel = String(value ?? '').trim().toLowerCase();
    switch (channel) {
      case 'email':
        return 'البريد الإلكتروني';
      default:
        return value ? 'قناة غير مدعومة' : 'غير محددة';
    }
  }

  private runSave(request$: any, successMessage: string) {
    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    request$.subscribe({
      next: () => {
        this.message.set(successMessage);
        this.refresh();
      },
      error: () => {
        this.error.set(this.i18n.text('Request failed.', 'فشل الطلب.'));
      },
      complete: () => this.saving.set(false)
    });
  }

  private parseEscalationPayload(raw: unknown): {
    runId: string;
    stageCode: string;
    stageName: string;
    escalatedToRole: string;
    period: string;
  } {
    const empty = {
      runId: '',
      stageCode: '',
      stageName: '',
      escalatedToRole: '',
      period: ''
    };

    if (typeof raw !== 'string' || !raw.trim()) {
      return empty;
    }

    try {
      const parsed = JSON.parse(raw);
      return {
        runId: String(parsed?.runId ?? ''),
        stageCode: String(parsed?.stageCode ?? ''),
        stageName: String(parsed?.stageName ?? ''),
        escalatedToRole: String(parsed?.escalatedToRole ?? ''),
        period: String(parsed?.period ?? '')
      };
    } catch {
      return empty;
    }
  }
}

