import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import {
  CreateOffboardingChecklistItemRequest,
  CreateOffboardingRequest,
  OffboardingChecklist,
  OffboardingRecord
} from '../models/offboarding.models';

@Injectable({ providedIn: 'root' })
export class OffboardingService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/offboarding`;

  list(employeeId?: string | null, status?: string | null) {
    let params = new HttpParams();
    if (employeeId) {
      params = params.set('employeeId', employeeId);
    }
    if (status) {
      params = params.set('status', status);
    }
    return this.http.get<OffboardingRecord[]>(this.base, { params });
  }

  create(request: CreateOffboardingRequest) {
    return this.http.post<{ id: string; status: string }>(this.base, request);
  }

  approve(offboardingId: string) {
    return this.http.post<{ id: string; status: string }>(`${this.base}/${offboardingId}/approve`, {});
  }

  getChecklist(offboardingId: string) {
    return this.http.get<OffboardingChecklist>(`${this.base}/${offboardingId}/checklist`);
  }

  addChecklistItem(offboardingId: string, request: CreateOffboardingChecklistItemRequest) {
    return this.http.post(`${this.base}/${offboardingId}/checklist/items`, request);
  }

  completeChecklistItem(offboardingId: string, itemId: string, notes?: string | null) {
    return this.http.post(`${this.base}/${offboardingId}/checklist/items/${itemId}/complete`, { notes: notes ?? null });
  }

  reopenChecklistItem(offboardingId: string, itemId: string) {
    return this.http.post(`${this.base}/${offboardingId}/checklist/items/${itemId}/reopen`, {});
  }
}
