export interface LoginRequest {
  tenantSlug: string;
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
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
