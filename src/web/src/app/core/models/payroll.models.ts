export interface PayrollPeriod {
  id: string;
  year: number;
  month: number;
  status: number;
  periodStartDate: string;
  periodEndDate: string;
}

export interface CreatePayrollPeriodRequest {
  year: number;
  month: number;
  periodStartDate: string;
  periodEndDate: string;
}

export interface PayrollAdjustment {
  id: string;
  employeeId: string;
  employeeName: string;
  year: number;
  month: number;
  type: number;
  amount: number;
  notes: string;
}

export interface CreatePayrollAdjustmentRequest {
  employeeId: string;
  year: number;
  month: number;
  type: number;
  amount: number;
  notes: string;
}

export interface PayrollRunDetails {
  id: string;
  payrollPeriodId: string;
  status: number;
  calculatedAtUtc?: string;
  approvedAtUtc?: string;
  lockedAtUtc?: string;
  lines: Array<{
    id: string;
    employeeId: string;
    employeeName: string;
    baseSalary: number;
    allowances: number;
    manualDeductions: number;
    loanDeduction: number;
    unpaidLeaveDays: number;
    unpaidLeaveDeduction: number;
    gosiWageBase: number;
    gosiEmployeeContribution: number;
    gosiEmployerContribution: number;
    deductions: number;
    overtimeHours: number;
    overtimeAmount: number;
    netAmount: number;
  }>;
}

export interface ExportJob {
  id: string;
  payrollRunId: string;
  employeeId?: string;
  artifactType: string;
  status: number;
  errorMessage?: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
  completedAtUtc?: string;
}

export interface EnqueueExportResponse {
  id: string;
  status: number;
}

export interface PayrollPreApprovalCheck {
  code: string;
  severity: 'Critical' | 'Warning' | 'Notice';
  employeeId?: string;
  employeeName?: string;
  message: string;
  metricName?: string;
  metricValue?: number;
}

export interface PayrollPreApprovalChecksResult {
  runId: string;
  hasBlockingFindings: boolean;
  generatedAtUtc: string;
  findings: PayrollPreApprovalCheck[];
}

export interface PayrollComplianceReadinessResult {
  runId: string;
  hasBlockingFindings: boolean;
  generatedAtUtc: string;
  findings: PayrollPreApprovalCheck[];
}

export interface PayrollApprovalDecision {
  id: string;
  createdAtUtc: string;
  decisionType: 'Standard' | 'Override';
  userId?: string;
  category?: string;
  referenceId?: string;
  reason?: string;
  criticalCodes?: string;
  warningCount?: string;
  findingsSnapshotJson?: string;
  findingsCount?: number;
}

export interface PayrollApprovalFindingSnapshot {
  code: string;
  severity: 'Critical' | 'Warning' | 'Notice' | string;
  employeeId?: string;
  employeeName?: string;
  message: string;
  metricName?: string;
  metricValue?: number;
}

export interface PayrollExecutiveSummary {
  runId: string;
  periodYear: number;
  periodMonth: number;
  employeeCount: number;
  currencyCode: string;
  totalNet: number;
  totalDeductions: number;
  totalOvertime: number;
  totalUnpaidLeaveDeduction: number;
  previousTotalNet?: number | null;
  deltaAmount?: number | null;
  deltaPercent?: number | null;
  summaryEn: string;
  summaryAr: string;
}
