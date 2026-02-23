import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import { MyPayslipItem, MyProfileResponse } from '../models/me.models';

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
}
