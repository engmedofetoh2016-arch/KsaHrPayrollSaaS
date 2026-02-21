import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs/operators';
import { apiConfig } from '../config/api.config';
import { AttendanceInputRow, UpsertAttendanceInputRequest } from '../models/attendance.models';

@Injectable({ providedIn: 'root' })
export class AttendanceService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/attendance-inputs`;

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
}
