import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map } from 'rxjs/operators';
import { apiConfig } from '../config/api.config';
import { AppUser, CreateUserRequest } from '../models/user.models';

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly base = `${apiConfig.baseUrl}/api/users`;

  list(page = 1, pageSize = 100, search = '') {
    const query = `?page=${page}&pageSize=${pageSize}&search=${encodeURIComponent(search)}`;
    return this.http.get<{ items: AppUser[] } | AppUser[]>(`${this.base}${query}`).pipe(
      map((response) => (Array.isArray(response) ? response : response.items ?? []))
    );
  }

  create(request: CreateUserRequest) {
    return this.http.post<AppUser>(this.base, request);
  }
}
