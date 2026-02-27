import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
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
  readonly error = signal('');
  readonly message = signal('');

  readonly allowanceMatrix = signal<any[]>([]);
  readonly approvalMatrix = signal<any[]>([]);
  readonly complianceRules = signal<any[]>([]);
  readonly forecastScenarios = signal<any[]>([]);
  readonly notificationTemplates = signal<any[]>([]);
  readonly notificationQueue = signal<any[]>([]);
  readonly dataQualityIssues = signal<any[]>([]);

  readonly allowanceForm = {
    policyName: 'Standard',
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
    stageName: 'Reviewer',
    stageOrder: 1,
    approverRole: 'Manager',
    allowRollback: true,
    isActive: true
  };

  readonly ruleForm = {
    ruleCode: 'WPS_MISSING',
    ruleName: 'WPS Missing Data',
    ruleCategory: 'WPS',
    severity: 'Critical',
    ruleConfigJson: '{"daysThreshold":3}',
    isEnabled: true
  };

  readonly forecastForm = {
    scenarioName: 'Monthly What-If',
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
    subject: 'Contract expiring soon',
    body: 'Your contract will expire soon. Please review.',
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

  ngOnInit(): void {
    this.refresh();
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
      notificationTemplates: this.http.get<any>(`${this.base}/notifications/templates`).pipe(catchError(() => of({ items: [] }))),
      notificationQueue: this.http.get<any>(`${this.base}/notifications/queue?take=50`).pipe(catchError(() => of({ items: [] }))),
      dataQualityIssues: this.http.get<any>(`${this.base}/payroll/data-quality/issues?status=Open&take=100`).pipe(catchError(() => of({ items: [] })))
    }).subscribe({
      next: (response) => {
        this.allowanceMatrix.set(Array.isArray(response.allowanceMatrix) ? response.allowanceMatrix : []);
        this.approvalMatrix.set(Array.isArray(response.approvalMatrix) ? response.approvalMatrix : []);
        this.complianceRules.set(Array.isArray(response.complianceRules?.items) ? response.complianceRules.items : []);
        this.forecastScenarios.set(Array.isArray(response.forecastScenarios?.items) ? response.forecastScenarios.items : []);
        this.notificationTemplates.set(Array.isArray(response.notificationTemplates?.items) ? response.notificationTemplates.items : []);
        this.notificationQueue.set(Array.isArray(response.notificationQueue?.items) ? response.notificationQueue.items : []);
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
        channel: this.templateForm.channel,
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
        channel: this.queueForm.channel,
        templateCode: this.queueForm.templateCode,
        relatedEntityType: this.queueForm.relatedEntityType,
        relatedEntityId: this.queueForm.relatedEntityId || null,
        payloadJson: this.queueForm.payloadJson || '{}',
        scheduledAtUtc: null
      }),
      this.i18n.text('Notification queued.', 'تمت إضافة الإشعار إلى قائمة الانتظار.')
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
}
