import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import {
  CreatePayrollAdjustmentRequest,
  CreatePayrollPeriodRequest,
  EnqueueExportResponse,
  ExportJob,
  PayrollAdjustment,
  PayrollApprovalDecision,
  PayrollExecutiveSummary,
  PayrollPreApprovalChecksResult,
  PayrollPeriod,
  PayrollRunDetails
} from '../models/payroll.models';

@Injectable({ providedIn: 'root' })
export class PayrollService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/payroll`;

  listPeriods() {
    return this.http.get<PayrollPeriod[]>(`${this.base}/periods`);
  }

  createPeriod(request: CreatePayrollPeriodRequest) {
    return this.http.post<PayrollPeriod>(`${this.base}/periods`, request);
  }

  listAdjustments(year: number, month: number) {
    const params = new HttpParams().set('year', year).set('month', month);
    return this.http.get<PayrollAdjustment[]>(`${this.base}/adjustments`, { params });
  }

  createAdjustment(request: CreatePayrollAdjustmentRequest) {
    return this.http.post(`${this.base}/adjustments`, request);
  }

  calculateRun(payrollPeriodId: string) {
    return this.http.post<{ runId: string; status: number }>(`${this.base}/runs/calculate`, { payrollPeriodId });
  }

  getRun(runId: string) {
    return this.http.get<PayrollRunDetails>(`${this.base}/runs/${runId}`);
  }

  getExecutiveSummary(runId: string) {
    return this.http.get<PayrollExecutiveSummary>(`${this.base}/runs/${runId}/executive-summary`);
  }

  approveRun(runId: string) {
    return this.http.post(`${this.base}/runs/${runId}/approve`, {});
  }

  approveRunWithOverride(runId: string, category: string, reason: string, referenceId: string) {
    return this.http.post(`${this.base}/runs/${runId}/approve-override`, { category, reason, referenceId });
  }

  getPreApprovalChecks(runId: string) {
    return this.http.get<PayrollPreApprovalChecksResult>(`${this.base}/runs/${runId}/pre-approval-checks`);
  }

  getApprovalDecisions(runId: string) {
    return this.http.get<{ items: PayrollApprovalDecision[] }>(`${this.base}/runs/${runId}/approval-decisions`);
  }

  getNextOverrideReferenceId() {
    return this.http.get<{ monthKey: string; nextSequence: number; referenceId: string; isSequenceExhausted: boolean }>(
      `${this.base}/governance/next-reference-id`
    );
  }

  lockRun(runId: string) {
    return this.http.post(`${this.base}/runs/${runId}/lock`, {});
  }

  queueRegisterCsvExport(runId: string) {
    return this.http.post<EnqueueExportResponse>(`${this.base}/runs/${runId}/exports/register-csv`, {});
  }

  queueGosiCsvExport(runId: string) {
    return this.http.post<EnqueueExportResponse>(`${this.base}/runs/${runId}/exports/gosi-csv`, {});
  }

  queueWpsCsvExport(runId: string) {
    return this.http.post<EnqueueExportResponse>(`${this.base}/runs/${runId}/exports/wps-csv`, {});
  }

  queuePayslipPdfExport(runId: string, employeeId: string) {
    return this.http.post<EnqueueExportResponse>(`${this.base}/runs/${runId}/exports/payslip/${employeeId}/pdf`, {});
  }

  listRunExports(runId: string) {
    return this.http.get<ExportJob[]>(`${this.base}/runs/${runId}/exports`);
  }

  getExport(exportId: string) {
    return this.http.get<ExportJob>(`${this.base}/exports/${exportId}`);
  }

  downloadExport(exportId: string) {
    return this.http.get(`${this.base}/exports/${exportId}/download`, { responseType: 'blob' });
  }
}
