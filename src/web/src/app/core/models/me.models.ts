export interface MyProfileUser {
  id: string;
  tenantId: string;
  firstName: string;
  lastName: string;
  email: string;
}

export interface MyProfileEmployee {
  id: string;
  startDate: string;
  firstName: string;
  lastName: string;
  email: string;
  jobTitle: string;
  baseSalary: number;
  employeeNumber: string;
  bankName: string;
  bankIban: string;
  iqamaNumber: string;
  iqamaExpiryDate?: string | null;
  workPermitExpiryDate?: string | null;
  contractEndDate?: string | null;
}

export interface MyProfileResponse {
  user: MyProfileUser;
  employee?: MyProfileEmployee | null;
}

export interface MyPayslipItem {
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
  periodYear: number;
  periodMonth: number;
}

export interface MyEosEstimateResponse {
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
