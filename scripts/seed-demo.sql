-- Demo SQL seed for HrPayroll (run after migrations)
CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $$
DECLARE
  v_tenant_id uuid;
  v_company_id uuid;
  v_period_id uuid;
  v_run_id uuid;
  v_year int := EXTRACT(YEAR FROM CURRENT_DATE);
  v_month int := EXTRACT(MONTH FROM CURRENT_DATE);

  v_emp_ahmed uuid;
  v_emp_sara uuid;
  v_emp_omar uuid;
  v_emp_mina uuid;
BEGIN
  SELECT "Id" INTO v_tenant_id
  FROM "TenantSet"
  WHERE "Slug" = 'demo-company'
  LIMIT 1;

  IF v_tenant_id IS NULL THEN
    v_tenant_id := gen_random_uuid();
    INSERT INTO "TenantSet" ("Id","Name","Slug","IsActive","CreatedAtUtc","UpdatedAtUtc")
    VALUES (v_tenant_id,'Demo Company','demo-company',true,now(),now());
  END IF;

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
      gen_random_uuid(), v_tenant_id, 'Demo Company LLC', 'SAR', 25,
      0.5, 1.0,
      'Al Rajhi Bank', 'RJHISARI', 'SA0380000000608010167519',
      false, '', 'Weekly', 6,
      null, now(), now()
    );
  END IF;

  INSERT INTO "EmployeeSet" (
    "Id","TenantId","FirstName","LastName","Email","JobTitle","BaseSalary",
    "IsSaudiNational","IsGosiEligible","GosiBasicWage","GosiHousingAllowance",
    "EmployeeNumber","BankName","BankIban",
    "IqamaNumber","IqamaExpiryDate","WorkPermitExpiryDate",
    "StartDate","CreatedAtUtc","UpdatedAtUtc"
  )
  SELECT gen_random_uuid(), v_tenant_id, 'Ahmed', 'Alharbi', 'ahmed.demo@demo.co', 'HR Specialist', 8200,
         true, true, 6500, 1200,
         'EMP-1001', 'Al Rajhi Bank', 'SA0380000000608010167519',
         '', null, null,
         CURRENT_DATE - INTERVAL '6 years', now(), now()
  WHERE NOT EXISTS (
    SELECT 1 FROM "EmployeeSet" WHERE "TenantId"=v_tenant_id AND "Email"='ahmed.demo@demo.co'
  );

  INSERT INTO "EmployeeSet" (
    "Id","TenantId","FirstName","LastName","Email","JobTitle","BaseSalary",
    "IsSaudiNational","IsGosiEligible","GosiBasicWage","GosiHousingAllowance",
    "EmployeeNumber","BankName","BankIban",
    "IqamaNumber","IqamaExpiryDate","WorkPermitExpiryDate",
    "StartDate","CreatedAtUtc","UpdatedAtUtc"
  )
  SELECT gen_random_uuid(), v_tenant_id, 'Sara', 'Alghamdi', 'sara.demo@demo.co', 'Accountant', 9800,
         true, true, 7800, 1500,
         'EMP-1002', 'Saudi National Bank', 'SA4420000001234567891234',
         '', null, null,
         CURRENT_DATE - INTERVAL '3 years', now(), now()
  WHERE NOT EXISTS (
    SELECT 1 FROM "EmployeeSet" WHERE "TenantId"=v_tenant_id AND "Email"='sara.demo@demo.co'
  );

  INSERT INTO "EmployeeSet" (
    "Id","TenantId","FirstName","LastName","Email","JobTitle","BaseSalary",
    "IsSaudiNational","IsGosiEligible","GosiBasicWage","GosiHousingAllowance",
    "EmployeeNumber","BankName","BankIban",
    "IqamaNumber","IqamaExpiryDate","WorkPermitExpiryDate",
    "StartDate","CreatedAtUtc","UpdatedAtUtc"
  )
  SELECT gen_random_uuid(), v_tenant_id, 'Omar', 'Nasser', 'omar.demo@demo.co', 'Engineer', 12500,
         false, true, 9800, 2200,
         'EMP-1003', 'Riyad Bank', 'SA6520000009876543210001',
         '2456789012', CURRENT_DATE + 42, CURRENT_DATE + 58,
         CURRENT_DATE - INTERVAL '8 years', now(), now()
  WHERE NOT EXISTS (
    SELECT 1 FROM "EmployeeSet" WHERE "TenantId"=v_tenant_id AND "Email"='omar.demo@demo.co'
  );

  INSERT INTO "EmployeeSet" (
    "Id","TenantId","FirstName","LastName","Email","JobTitle","BaseSalary",
    "IsSaudiNational","IsGosiEligible","GosiBasicWage","GosiHousingAllowance",
    "EmployeeNumber","BankName","BankIban",
    "IqamaNumber","IqamaExpiryDate","WorkPermitExpiryDate",
    "StartDate","CreatedAtUtc","UpdatedAtUtc"
  )
  SELECT gen_random_uuid(), v_tenant_id, 'Mina', 'Ibrahim', 'mina.demo@demo.co', 'Operations Coordinator', 7300,
         false, false, 0, 0,
         'EMP-1004', 'Banque Saudi Fransi', 'SA5515000060045678901234',
         '2987654321', CURRENT_DATE + 18, CURRENT_DATE + 33,
         CURRENT_DATE - INTERVAL '2 years', now(), now()
  WHERE NOT EXISTS (
    SELECT 1 FROM "EmployeeSet" WHERE "TenantId"=v_tenant_id AND "Email"='mina.demo@demo.co'
  );

  SELECT "Id" INTO v_emp_ahmed FROM "EmployeeSet" WHERE "TenantId"=v_tenant_id AND "Email"='ahmed.demo@demo.co' LIMIT 1;
  SELECT "Id" INTO v_emp_sara  FROM "EmployeeSet" WHERE "TenantId"=v_tenant_id AND "Email"='sara.demo@demo.co' LIMIT 1;
  SELECT "Id" INTO v_emp_omar  FROM "EmployeeSet" WHERE "TenantId"=v_tenant_id AND "Email"='omar.demo@demo.co' LIMIT 1;
  SELECT "Id" INTO v_emp_mina  FROM "EmployeeSet" WHERE "TenantId"=v_tenant_id AND "Email"='mina.demo@demo.co' LIMIT 1;

  INSERT INTO "AttendanceInputSet" ("Id","TenantId","EmployeeId","Year","Month","DaysPresent","DaysAbsent","OvertimeHours","CreatedAtUtc","UpdatedAtUtc")
  SELECT gen_random_uuid(), v_tenant_id, v_emp_ahmed, v_year, v_month, 22, 0, 10, now(), now()
  WHERE NOT EXISTS (SELECT 1 FROM "AttendanceInputSet" WHERE "TenantId"=v_tenant_id AND "EmployeeId"=v_emp_ahmed AND "Year"=v_year AND "Month"=v_month);

  INSERT INTO "AttendanceInputSet" ("Id","TenantId","EmployeeId","Year","Month","DaysPresent","DaysAbsent","OvertimeHours","CreatedAtUtc","UpdatedAtUtc")
  SELECT gen_random_uuid(), v_tenant_id, v_emp_sara, v_year, v_month, 21, 1, 4, now(), now()
  WHERE NOT EXISTS (SELECT 1 FROM "AttendanceInputSet" WHERE "TenantId"=v_tenant_id AND "EmployeeId"=v_emp_sara AND "Year"=v_year AND "Month"=v_month);

  INSERT INTO "AttendanceInputSet" ("Id","TenantId","EmployeeId","Year","Month","DaysPresent","DaysAbsent","OvertimeHours","CreatedAtUtc","UpdatedAtUtc")
  SELECT gen_random_uuid(), v_tenant_id, v_emp_omar, v_year, v_month, 20, 2, 12, now(), now()
  WHERE NOT EXISTS (SELECT 1 FROM "AttendanceInputSet" WHERE "TenantId"=v_tenant_id AND "EmployeeId"=v_emp_omar AND "Year"=v_year AND "Month"=v_month);

  INSERT INTO "AttendanceInputSet" ("Id","TenantId","EmployeeId","Year","Month","DaysPresent","DaysAbsent","OvertimeHours","CreatedAtUtc","UpdatedAtUtc")
  SELECT gen_random_uuid(), v_tenant_id, v_emp_mina, v_year, v_month, 22, 0, 6, now(), now()
  WHERE NOT EXISTS (SELECT 1 FROM "AttendanceInputSet" WHERE "TenantId"=v_tenant_id AND "EmployeeId"=v_emp_mina AND "Year"=v_year AND "Month"=v_month);

  SELECT "Id" INTO v_period_id
  FROM "PayrollPeriodSet"
  WHERE "TenantId"=v_tenant_id AND "Year"=v_year AND "Month"=v_month
  LIMIT 1;

  IF v_period_id IS NULL THEN
    v_period_id := gen_random_uuid();
    INSERT INTO "PayrollPeriodSet" ("Id","TenantId","Year","Month","Status","PeriodStartDate","PeriodEndDate","CreatedAtUtc","UpdatedAtUtc")
    VALUES (
      v_period_id, v_tenant_id, v_year, v_month, 0,
      make_date(v_year, v_month, 1),
      (make_date(v_year, v_month, 1) + interval '1 month - 1 day')::date,
      now(), now()
    );
  END IF;

  SELECT "Id" INTO v_run_id
  FROM "PayrollRunSet"
  WHERE "TenantId"=v_tenant_id AND "PayrollPeriodId"=v_period_id
  LIMIT 1;

  IF v_run_id IS NULL THEN
    v_run_id := gen_random_uuid();
    INSERT INTO "PayrollRunSet" ("Id","TenantId","PayrollPeriodId","Status","CalculatedAtUtc","ApprovedAtUtc","LockedAtUtc","CreatedAtUtc","UpdatedAtUtc")
    VALUES (v_run_id, v_tenant_id, v_period_id, 0, null, null, null, now(), now());
  END IF;

  INSERT INTO "PayrollAdjustmentSet" ("Id","TenantId","EmployeeId","Year","Month","Type","Amount","Notes","CreatedAtUtc","UpdatedAtUtc")
  SELECT gen_random_uuid(), v_tenant_id, v_emp_ahmed, v_year, v_month, 1, 600, 'Demo allowance', now(), now()
  WHERE NOT EXISTS (SELECT 1 FROM "PayrollAdjustmentSet" WHERE "TenantId"=v_tenant_id AND "EmployeeId"=v_emp_ahmed AND "Year"=v_year AND "Month"=v_month AND "Type"=1);

  INSERT INTO "PayrollAdjustmentSet" ("Id","TenantId","EmployeeId","Year","Month","Type","Amount","Notes","CreatedAtUtc","UpdatedAtUtc")
  SELECT gen_random_uuid(), v_tenant_id, v_emp_sara, v_year, v_month, 2, 120, 'Demo deduction', now(), now()
  WHERE NOT EXISTS (SELECT 1 FROM "PayrollAdjustmentSet" WHERE "TenantId"=v_tenant_id AND "EmployeeId"=v_emp_sara AND "Year"=v_year AND "Month"=v_month AND "Type"=2);

  INSERT INTO "LeaveRequestSet" ("Id","TenantId","EmployeeId","LeaveType","StartDate","EndDate","TotalDays","Reason","Status","RejectionReason","ReviewedByUserId","ReviewedAtUtc","CreatedAtUtc","UpdatedAtUtc")
  SELECT gen_random_uuid(), v_tenant_id, v_emp_ahmed, 3, CURRENT_DATE + 10, CURRENT_DATE + 12, 3, 'Demo unpaid leave', 2, null, null, now(), now(), now()
  WHERE NOT EXISTS (
    SELECT 1 FROM "LeaveRequestSet"
    WHERE "TenantId"=v_tenant_id AND "EmployeeId"=v_emp_ahmed AND "LeaveType"=3 AND "StartDate"=CURRENT_DATE + 10
  );

  INSERT INTO "ComplianceAlertSet" (
    "Id","TenantId","EmployeeId","EmployeeName","IsSaudiNational","DocumentType","DocumentNumber",
    "ExpiryDate","DaysLeft","Severity","IsResolved","ResolveReason","ResolvedByUserId","ResolvedAtUtc",
    "LastDetectedAtUtc","CreatedAtUtc","UpdatedAtUtc"
  )
  SELECT gen_random_uuid(), v_tenant_id, v_emp_omar, 'Omar Nasser', false, 'Iqama', '2456789012',
         CURRENT_DATE + 42, 42, 'Warning', false, '', null, null, now(), now(), now()
  WHERE NOT EXISTS (
    SELECT 1 FROM "ComplianceAlertSet"
    WHERE "TenantId"=v_tenant_id AND "EmployeeId"=v_emp_omar AND "DocumentType"='Iqama' AND "ExpiryDate"=CURRENT_DATE + 42
  );
END $$;
