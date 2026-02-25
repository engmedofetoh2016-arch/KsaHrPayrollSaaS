import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import { MyEosEstimateResponse, MyPayslipItem, MyProfileResponse } from '../models/me.models';

@Injectable({ providedIn: 'root' })
export class MeService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/me`;

  getProfile() {
    return this.http.get<MyProfileResponse>(`${this.base}/profile`);
  }

  listPayslips() {
    return this.http.get<MyPayslipItem[]>(`${this.base}/payslips`);
  }

  getLatestPayslip() {
    return this.http.get<MyPayslipItem | null>(`${this.base}/payslips/latest`);
  }

  downloadPayslip(exportId: string) {
    return this.http.get(`${this.base}/payslips/${exportId}/download`, { responseType: 'blob' });
  }

  downloadSalaryCertificate(purpose?: string | null) {
    const query = purpose && purpose.trim().length > 0
      ? `?purpose=${encodeURIComponent(purpose.trim())}`
      : '';
    return this.http.get(`${this.base}/salary-certificate/pdf${query}`, { responseType: 'blob' });
  }

  getEosEstimate(terminationDate?: string | null) {
    const query = terminationDate && terminationDate.trim().length > 0
      ? `?terminationDate=${encodeURIComponent(terminationDate.trim())}`
      : '';
    return this.http.get<MyEosEstimateResponse>(`${this.base}/eos-estimate${query}`);
  }
}
