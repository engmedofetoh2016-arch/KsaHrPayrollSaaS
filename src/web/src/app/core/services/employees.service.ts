import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs/operators';
import { apiConfig } from '../config/api.config';
import {
  CreateEmployeeLoginRequest,
  CreateEmployeeRequest,
  Employee,
  EosEstimateResult,
  FinalSettlementExportJob,
  FinalSettlementEstimateRequest,
  FinalSettlementEstimateResult,
  UpdateEmployeeRequest
} from '../models/employee.models';

@Injectable({ providedIn: 'root' })
export class EmployeesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/employees`;

  list(page = 1, pageSize = 100, search = '') {
    const query = `?page=${page}&pageSize=${pageSize}&search=${encodeURIComponent(search)}`;
    return this.http.get<{ items: Employee[] } | Employee[]>(`${this.base}${query}`).pipe(
      map((response) => (Array.isArray(response) ? response : response.items ?? []))
    );
  }

  create(request: CreateEmployeeRequest) {
    return this.http.post<Employee>(this.base, request);
  }

  update(employeeId: string, request: UpdateEmployeeRequest) {
    return this.http.put<Employee>(`${this.base}/${employeeId}`, request);
  }

  delete(employeeId: string) {
    return this.http.delete<void>(`${this.base}/${employeeId}`);
  }

  createUserLogin(employeeId: string, request: CreateEmployeeLoginRequest) {
    return this.http.post<{ id: string; email: string; firstName: string; lastName: string; role: string }>(
      `${this.base}/${employeeId}/create-user-login`,
      request
    );
  }

  downloadSalaryCertificate(employeeId: string, purpose?: string | null) {
    const query = purpose && purpose.trim().length > 0
      ? `?purpose=${encodeURIComponent(purpose.trim())}`
      : '';
    return this.http.get(`${this.base}/${employeeId}/salary-certificate/pdf${query}`, {
      observe: 'response',
      responseType: 'blob'
    });
  }

  estimateEos(employeeId: string, terminationDate?: string) {
    return this.http.post<EosEstimateResult>(`${this.base}/${employeeId}/eos-estimate`, {
      terminationDate: terminationDate || null
    });
  }

  estimateFinalSettlement(employeeId: string, request: FinalSettlementEstimateRequest) {
    return this.http.post<FinalSettlementEstimateResult>(`${this.base}/${employeeId}/final-settlement/estimate`, request);
  }

  exportFinalSettlementCsv(employeeId: string, request: FinalSettlementEstimateRequest) {
    return this.http.post(`${this.base}/${employeeId}/final-settlement/export-csv`, request, {
      observe: 'response',
      responseType: 'blob'
    });
  }

  exportFinalSettlementPdf(employeeId: string, request: FinalSettlementEstimateRequest) {
    return this.http.post(`${this.base}/${employeeId}/final-settlement/export-pdf`, request, {
      observe: 'response',
      responseType: 'blob'
    });
  }

  queueFinalSettlementPdfExport(employeeId: string, request: FinalSettlementEstimateRequest) {
    return this.http.post<{ id: string; status: number }>(`${this.base}/${employeeId}/final-settlement/exports/pdf`, request);
  }

  listFinalSettlementExports(employeeId: string) {
    return this.http.get<FinalSettlementExportJob[]>(`${this.base}/${employeeId}/final-settlement/exports`);
  }

  listFinalSettlementExportsGlobal(employeeId?: string, status?: number, take = 100) {
    const params = new URLSearchParams();
    params.set('take', String(take));
    if (employeeId) {
      params.set('employeeId', employeeId);
    }
    if (typeof status === 'number' && status > 0) {
      params.set('status', String(status));
    }
    return this.http.get<FinalSettlementExportJob[]>(`${apiConfig.baseUrl}/api/final-settlement/exports?${params.toString()}`);
  }

  getFinalSettlementExport(exportId: string) {
    return this.http.get<FinalSettlementExportJob>(`${apiConfig.baseUrl}/api/final-settlement/exports/${exportId}`);
  }

  downloadFinalSettlementExport(exportId: string) {
    return this.http.get(`${apiConfig.baseUrl}/api/final-settlement/exports/${exportId}/download`, {
      observe: 'response',
      responseType: 'blob'
    });
  }

  retryFinalSettlementExport(exportId: string) {
    return this.http.post<{ id: string; status: number }>(`${apiConfig.baseUrl}/api/final-settlement/exports/${exportId}/retry`, {});
  }

  downloadFinalSettlementExportZip(employeeId: string) {
    return this.http.get(`${this.base}/${employeeId}/final-settlement/exports/zip`, {
      observe: 'response',
      responseType: 'blob'
    });
  }

  downloadFinalSettlementExportZipGlobal(employeeId?: string) {
    const params = new URLSearchParams();
    if (employeeId) {
      params.set('employeeId', employeeId);
    }
    const query = params.toString();
    const url = `${apiConfig.baseUrl}/api/final-settlement/exports/zip${query ? `?${query}` : ''}`;
    return this.http.get(url, {
      observe: 'response',
      responseType: 'blob'
    });
  }
}
