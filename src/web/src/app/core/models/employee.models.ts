export interface Employee {
  id: string;
  startDate: string;
  firstName: string;
  lastName: string;
  email: string;
  jobTitle: string;
  baseSalary: number;
  isSaudiNational: boolean;
  isGosiEligible: boolean;
  gosiBasicWage: number;
  gosiHousingAllowance: number;
  employeeNumber: string;
  bankName: string;
  bankIban: string;
  iqamaNumber: string;
  iqamaExpiryDate?: string | null;
  workPermitExpiryDate?: string | null;
  tenantId: string;
}

export interface CreateEmployeeRequest {
  startDate: string;
  firstName: string;
  lastName: string;
  email: string;
  jobTitle: string;
  baseSalary: number;
  isSaudiNational: boolean;
  isGosiEligible: boolean;
  gosiBasicWage: number;
  gosiHousingAllowance: number;
  employeeNumber: string;
  bankName: string;
  bankIban: string;
  iqamaNumber: string;
  iqamaExpiryDate?: string | null;
  workPermitExpiryDate?: string | null;
}

export interface EosEstimateResult {
  id: string;
  startDate: string;
  baseSalary: number;
  terminationDate: string;
  serviceDays: number;
  serviceYears: number;
  firstYears: number;
  remainingYears: number;
  eosFirstFiveYearsMonthFactor: number;
  eosAfterFiveYearsMonthFactor: number;
  eosMonths: number;
  eosAmount: number;
  currencyCode: string;
}

export interface FinalSettlementEstimateRequest {
  terminationDate: string;
  year?: number | null;
  month?: number | null;
  additionalManualDeduction: number;
  notes?: string | null;
}

export interface FinalSettlementEstimateResult {
  id: string;
  employeeName: string;
  startDate: string;
  terminationDate: string;
  periodYear: number;
  periodMonth: number;
  baseSalary: number;
  serviceDays: number;
  serviceYears: number;
  eosMonths: number;
  eosAmount: number;
  unpaidLeaveDays: number;
  unpaidLeaveDeduction: number;
  manualDeductionsFromPayroll: number;
  additionalManualDeduction: number;
  totalDeductions: number;
  netSettlement: number;
  notes: string | null;
  currencyCode: string;
}

export interface FinalSettlementExportJob {
  id: string;
  employeeId: string;
  employeeName?: string | null;
  artifactType: string;
  fileName: string;
  contentType: string;
  status: number;
  sizeBytes: number;
  errorMessage: string | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
}
