import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs/operators';
import { apiConfig } from '../config/api.config';
import { AttendanceInputRow, ManualBookingRequest, MyBookingRow, TimesheetBookingRow, UpsertAttendanceInputRequest } from '../models/attendance.models';

@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/attendance-inputs`;
  private readonly myBookingsBase = `${apiConfig.baseUrl}/api/me/bookings`;
  private readonly timesheetsBase = `${apiConfig.baseUrl}/api/timesheets`;

  list(year: number, month: number, page = 1, pageSize = 200, search = '') {
    const params = new HttpParams()
      .set('year', year)
      .set('month', month)
      .set('page', page)
      .set('pageSize', pageSize)
      .set('search', search);

    return this.http.get<{ items: AttendanceInputRow[] } | AttendanceInputRow[]>(this.base, { params }).pipe(
      map((response) => (Array.isArray(response) ? response : response.items ?? []))
    );
  }

  upsert(request: UpsertAttendanceInputRequest) {
    return this.http.post(this.base, request);
  }

  listMyBookings(from?: string, to?: string) {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }

    return this.http.get<{ items: MyBookingRow[] }>(this.myBookingsBase, { params }).pipe(
      map((response) => response.items ?? [])
    );
  }

  checkIn() {
    return this.http.post(`${this.myBookingsBase}/check-in`, {});
  }

  checkOut() {
    return this.http.post(`${this.myBookingsBase}/check-out`, {});
  }

  saveManualBooking(request: ManualBookingRequest) {
    return this.http.post(`${this.myBookingsBase}/manual`, request);
  }

  listTimesheets(year: number, month: number, employeeId?: string, status?: string) {
    let params = new HttpParams()
      .set('year', year)
      .set('month', month);

    if (employeeId) {
      params = params.set('employeeId', employeeId);
    }

    if (status) {
      params = params.set('status', status);
    }

    return this.http.get<TimesheetBookingRow[]>(this.timesheetsBase, { params });
  }
}
