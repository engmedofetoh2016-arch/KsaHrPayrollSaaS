import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs/operators';
import { apiConfig } from '../config/api.config';
import { AttendanceInputRow, MyBookingRow, UpsertAttendanceInputRequest } from '../models/attendance.models';

@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/attendance-inputs`;
  private readonly myBookingsBase = `${apiConfig.baseUrl}/api/me/bookings`;

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
}
