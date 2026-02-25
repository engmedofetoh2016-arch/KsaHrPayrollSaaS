export interface EmployeeLoan {
  id: string;
  employeeId: string;
  employeeName: string;
  loanType: string;
  principalAmount: number;
  remainingBalance: number;
  installmentAmount: number;
  startYear: number;
  startMonth: number;
  totalInstallments: number;
  paidInstallments: number;
  status: string;
  notes: string;
  createdAtUtc: string;
}

export interface CreateEmployeeLoanRequest {
  employeeId: string;
  loanType: string;
  principalAmount: number;
  installmentAmount: number;
  startYear: number;
  startMonth: number;
  totalInstallments: number;
  notes?: string | null;
}

export interface EmployeeLoanInstallment {
  id: string;
  year: number;
  month: number;
  amount: number;
  status: string;
  payrollRunId?: string | null;
  deductedAtUtc?: string | null;
}
