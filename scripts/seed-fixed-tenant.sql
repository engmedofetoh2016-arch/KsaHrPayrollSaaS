-- Fixed tenant SQL seed for HrPayroll
-- Creates/updates tenant + owner user + Owner role mapping
-- Target credentials:
--   TenantSlug: managm_elzahb
--   OwnerEmail: magdyKago@mangm.com
--   OwnerPassword: magdy123456

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $$
DECLARE
  v_tenant_id uuid;
  v_company_id uuid;
  v_owner_role_id uuid;
  v_user_id uuid;

  v_tenant_name text := 'Managm Elzahb';
  v_slug text := 'managm_elzahb';
  v_company_legal_name text := 'Managm Elzahb';
  v_currency_code text := 'SAR';
  v_default_pay_day int := 25;

  v_first_name text := 'Magdy';
  v_last_name text := 'Kago';
  v_email text := 'magdyKago@mangm.com';
  v_normalized_email text := upper('magdyKago@mangm.com');

  -- ASP.NET Identity v3 hash for password: magdy123456
  v_password_hash text := 'AQAAAAIAAYagAAAAEHK9/eDoSmkkJEZOg+KS/HlNSXcUGIeuarFJWExXEze0GGIK5SFnGPkvxCyok/F9VQ==';
BEGIN
  -- Tenant
  SELECT "Id" INTO v_tenant_id
  FROM "TenantSet"
  WHERE "Slug" = v_slug
  LIMIT 1;

  IF v_tenant_id IS NULL THEN
    v_tenant_id := gen_random_uuid();
    INSERT INTO "TenantSet" ("Id","Name","Slug","IsActive","CreatedAtUtc","UpdatedAtUtc")
    VALUES (v_tenant_id, v_tenant_name, v_slug, true, now(), now());
  ELSE
    UPDATE "TenantSet"
    SET "Name" = v_tenant_name,
        "IsActive" = true,
        "UpdatedAtUtc" = now()
    WHERE "Id" = v_tenant_id;
  END IF;

  -- Company profile
  SELECT "Id" INTO v_company_id
  FROM "CompanyProfileSet"
  WHERE "TenantId" = v_tenant_id
  LIMIT 1;

  IF v_company_id IS NULL THEN
    INSERT INTO "CompanyProfileSet" (
      "Id","TenantId","LegalName","CurrencyCode","DefaultPayDay",
      "EosFirstFiveYearsMonthFactor","EosAfterFiveYearsMonthFactor",
      "WpsCompanyBankName","WpsCompanyBankCode","WpsCompanyIban",
      "ComplianceDigestEnabled","ComplianceDigestEmail","ComplianceDigestFrequency","ComplianceDigestHourUtc",
      "LastComplianceDigestSentAtUtc","CreatedAtUtc","UpdatedAtUtc"
    )
    VALUES (
      gen_random_uuid(), v_tenant_id, v_company_legal_name, v_currency_code, v_default_pay_day,
      0.5, 1.0,
      'Al Rajhi Bank', 'RJHISARI', 'SA0380000000608010167519',
      false, '', 'Weekly', 6,
      null, now(), now()
    );
  ELSE
    UPDATE "CompanyProfileSet"
    SET "LegalName" = v_company_legal_name,
        "CurrencyCode" = v_currency_code,
        "DefaultPayDay" = v_default_pay_day,
        "UpdatedAtUtc" = now()
    WHERE "Id" = v_company_id;
  END IF;

  -- Ensure Owner role exists
  SELECT "Id" INTO v_owner_role_id
  FROM "AspNetRoles"
  WHERE "NormalizedName" = 'OWNER'
  LIMIT 1;

  IF v_owner_role_id IS NULL THEN
    v_owner_role_id := gen_random_uuid();
    INSERT INTO "AspNetRoles" ("Id","Name","NormalizedName","ConcurrencyStamp")
    VALUES (v_owner_role_id, 'Owner', 'OWNER', gen_random_uuid()::text);
  END IF;

  -- Owner user
  SELECT "Id" INTO v_user_id
  FROM "AspNetUsers"
  WHERE "NormalizedEmail" = v_normalized_email
  LIMIT 1;

  IF v_user_id IS NULL THEN
    v_user_id := gen_random_uuid();
    INSERT INTO "AspNetUsers" (
      "Id","TenantId","FirstName","LastName",
      "UserName","NormalizedUserName","Email","NormalizedEmail",
      "EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp",
      "PhoneNumber","PhoneNumberConfirmed","TwoFactorEnabled",
      "LockoutEnd","LockoutEnabled","AccessFailedCount"
    )
    VALUES (
      v_user_id, v_tenant_id, v_first_name, v_last_name,
      v_email, v_normalized_email, v_email, v_normalized_email,
      true, v_password_hash, gen_random_uuid()::text, gen_random_uuid()::text,
      null, false, false,
      null, false, 0
    );
  ELSE
    UPDATE "AspNetUsers"
    SET "TenantId" = v_tenant_id,
        "FirstName" = v_first_name,
        "LastName" = v_last_name,
        "UserName" = v_email,
        "NormalizedUserName" = v_normalized_email,
        "Email" = v_email,
        "NormalizedEmail" = v_normalized_email,
        "EmailConfirmed" = true,
        "PasswordHash" = v_password_hash,
        "SecurityStamp" = gen_random_uuid()::text,
        "ConcurrencyStamp" = gen_random_uuid()::text,
        "AccessFailedCount" = 0
    WHERE "Id" = v_user_id;
  END IF;

  -- Ensure Owner role mapping
  IF NOT EXISTS (
    SELECT 1
    FROM "AspNetUserRoles"
    WHERE "UserId" = v_user_id
      AND "RoleId" = v_owner_role_id
  ) THEN
    INSERT INTO "AspNetUserRoles" ("UserId","RoleId")
    VALUES (v_user_id, v_owner_role_id);
  END IF;

  RAISE NOTICE 'Seed ready: TenantSlug=%, OwnerEmail=%', v_slug, v_email;
END $$;
