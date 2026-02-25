import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import { CreateEmployeeLoanRequest, EmployeeLoan, EmployeeLoanInstallment } from '../models/loan.models';

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
}
