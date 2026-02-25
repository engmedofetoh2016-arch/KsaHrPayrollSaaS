import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import { SmartAlertActionRequest, SmartAlertExplainResponse, SmartAlertsResponse, SmartAlertSnoozeRequest } from '../models/smart-alert.models';

@Injectable({ providedIn: 'root' })
export class SmartAlertsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/smart-alerts`;

  list(daysAhead = 30) {
    const params = new HttpParams().set('daysAhead', Math.max(1, Math.min(120, daysAhead)));
    return this.http.get<SmartAlertsResponse>(this.base, { params });
  }

  acknowledge(key: string, request: SmartAlertActionRequest = {}) {
    return this.http.post(`${this.base}/${encodeURIComponent(key)}/acknowledge`, request);
  }

  snooze(key: string, request: SmartAlertSnoozeRequest) {
    return this.http.post(`${this.base}/${encodeURIComponent(key)}/snooze`, request);
  }

  explainRisk(key: string, language: string) {
    const params = new HttpParams().set('language', language || 'en');
    return this.http.get<SmartAlertExplainResponse>(`${this.base}/${encodeURIComponent(key)}/explain-risk`, { params });
  }
}
