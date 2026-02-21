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
