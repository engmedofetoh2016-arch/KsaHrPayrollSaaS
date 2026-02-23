export interface AppUser {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  tenantId: string;
  accessFailedCount: number;
  lockoutEnd?: string | null;
}

export interface CreateUserRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  role: string;
}
