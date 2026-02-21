export interface AppUser {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  tenantId: string;
}

export interface CreateUserRequest {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
  role: string;
}
