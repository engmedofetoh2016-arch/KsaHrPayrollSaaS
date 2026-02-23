export interface LoginRequest {
  tenantSlug: string;
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  mustChangePassword?: boolean;
  user: {
    id: string;
    email: string;
    firstName: string;
    lastName: string;
    tenantId: string;
    roles: string[];
  };
}

export interface AuthSession {
  accessToken: string;
  tenantId: string;
  roles: string[];
  email: string;
  firstName: string;
  lastName: string;
  mustChangePassword: boolean;
}

export interface SignUpRequest {
  tenantName: string;
  slug: string;
  companyLegalName: string;
  currencyCode: string;
  defaultPayDay: number;
  ownerFirstName: string;
  ownerLastName: string;
  ownerEmail: string;
  ownerPassword: string;
}

export interface ForgotPasswordRequest {
  tenantSlug: string;
  email: string;
}

export interface ResetPasswordRequest {
  tenantSlug: string;
  email: string;
  token: string;
  newPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}
