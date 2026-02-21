import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import { CompanyProfile, UpdateCompanyProfileRequest } from '../models/company.models';

@Injectable({ providedIn: 'root' })
export class CompanyService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/company-profile`;

  getProfile() {
    return this.http.get<CompanyProfile>(this.base);
  }

  updateProfile(request: UpdateCompanyProfileRequest) {
    return this.http.put<CompanyProfile>(this.base, request);
  }
}
