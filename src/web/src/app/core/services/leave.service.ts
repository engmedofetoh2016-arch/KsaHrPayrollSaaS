import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import {
  CreateLeaveRequestPayload,
  LeaveAttachment,
  LeaveBalance,
  LeaveBalancePreviewRequest,
  LeaveBalancePreviewResult,
  LeaveRequest
} from '../models/leave.models';

@Injectable({ providedIn: 'root' })
export class LeaveService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/leave`;

  listRequests(status?: number, employeeId?: string) {
    let params = new HttpParams();
    if (status !== undefined && status > 0) {
      params = params.set('status', status);
    }
    if (employeeId) {
      params = params.set('employeeId', employeeId);
    }
    return this.http.get<LeaveRequest[]>(`${this.base}/requests`, { params });
  }

  createRequest(payload: CreateLeaveRequestPayload) {
    return this.http.post<LeaveRequest>(`${this.base}/requests`, payload);
  }

  approveRequest(requestId: string, comment?: string) {
    return this.http.post(`${this.base}/requests/${requestId}/approve`, { comment: comment ?? null });
  }

  rejectRequest(requestId: string, rejectionReason: string) {
    return this.http.post(`${this.base}/requests/${requestId}/reject`, { rejectionReason });
  }

  listBalances(year: number, employeeId?: string) {
    let params = new HttpParams().set('year', year);
    if (employeeId) {
      params = params.set('employeeId', employeeId);
    }
    return this.http.get<LeaveBalance[]>(`${this.base}/balances`, { params });
  }

  previewBalance(payload: LeaveBalancePreviewRequest) {
    return this.http.post<LeaveBalancePreviewResult>(`${this.base}/preview`, payload);
  }

  listAttachments(requestId: string) {
    return this.http.get<LeaveAttachment[]>(`${this.base}/requests/${requestId}/attachments`);
  }

  uploadAttachment(requestId: string, file: File) {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<LeaveAttachment>(`${this.base}/requests/${requestId}/attachments`, formData);
  }

  downloadAttachment(attachmentId: string) {
    return this.http.get(`${this.base}/attachments/${attachmentId}/download`, {
      observe: 'response',
      responseType: 'blob'
    });
  }
}
