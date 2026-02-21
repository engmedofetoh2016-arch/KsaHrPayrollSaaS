import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiConfig } from '../config/api.config';
import { CreateLeaveRequestPayload, LeaveBalance, LeaveRequest } from '../models/leave.models';

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

  approveRequest(requestId: string) {
    return this.http.post(`${this.base}/requests/${requestId}/approve`, {});
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
}
