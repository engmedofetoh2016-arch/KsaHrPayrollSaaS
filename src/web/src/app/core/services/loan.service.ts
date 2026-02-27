import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import {
  CreateEmployeeLoanRequest,
  EmployeeLoan,
  EmployeeLoanInstallment,
  EmployeeLoanLifecycleCheck,
  RescheduleEmployeeLoanRequest,
  SettleEmployeeLoanRequest,
  SkipEmployeeLoanInstallmentRequest
} from '../models/loan.models';

@Injectable({ providedIn: 'root' })
export class LoanService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/payroll/loans`;

  list(employeeId?: string | null, status?: string | null) {
    let params = new HttpParams();
    if (employeeId) {
      params = params.set('employeeId', employeeId);
    }
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<EmployeeLoan[]>(this.base, { params });
  }

  create(request: CreateEmployeeLoanRequest) {
    return this.http.post<{ id: string; status: string }>(this.base, request);
  }

  approve(loanId: string) {
    return this.http.post<{ id: string; status: string }>(`${this.base}/${loanId}/approve`, {});
  }

  cancel(loanId: string) {
    return this.http.post<{ id: string; status: string }>(`${this.base}/${loanId}/cancel`, {});
  }

  listInstallments(loanId: string) {
    return this.http.get<EmployeeLoanInstallment[]>(`${this.base}/${loanId}/installments`);
  }

  lifecycleCheck(loanId: string) {
    return this.http.get<EmployeeLoanLifecycleCheck>(`${this.base}/${loanId}/lifecycle-check`);
  }

  reschedule(loanId: string, request: RescheduleEmployeeLoanRequest) {
    return this.http.post<{ id: string; status: string }>(`${this.base}/${loanId}/reschedule`, request);
  }

  skipNext(loanId: string, request: SkipEmployeeLoanInstallmentRequest) {
    return this.http.post<{ id: string; status: string }>(`${this.base}/${loanId}/skip-next`, request);
  }

  settleEarly(loanId: string, request: SettleEmployeeLoanRequest) {
    return this.http.post<{ id: string; status: string; remainingBalance: number }>(`${this.base}/${loanId}/settle-early`, request);
  }
}
