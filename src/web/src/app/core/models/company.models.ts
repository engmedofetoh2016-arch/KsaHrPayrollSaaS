export interface CompanyProfile {
  id: string;
  tenantId: string;
  legalName: string;
  currencyCode: string;
  defaultPayDay: number;
  eosFirstFiveYearsMonthFactor: number;
  eosAfterFiveYearsMonthFactor: number;
  wpsCompanyBankName: string;
  wpsCompanyBankCode: string;
  wpsCompanyIban: string;
  complianceDigestEnabled: boolean;
  complianceDigestEmail: string;
  complianceDigestFrequency: string;
  complianceDigestHourUtc: number;
  lastComplianceDigestSentAtUtc?: string | null;
}

export interface UpdateCompanyProfileRequest {
  legalName: string;
  currencyCode: string;
  defaultPayDay: number;
  eosFirstFiveYearsMonthFactor: number;
  eosAfterFiveYearsMonthFactor: number;
  wpsCompanyBankName: string;
  wpsCompanyBankCode: string;
  wpsCompanyIban: string;
  complianceDigestEnabled: boolean;
  complianceDigestEmail: string;
  complianceDigestFrequency: string;
  complianceDigestHourUtc: number;
}
