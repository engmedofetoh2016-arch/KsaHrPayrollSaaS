using FluentValidation;
using HrPayroll.Api.Validation;
using HrPayroll.Api.Middleware;
using HrPayroll.Application;
using HrPayroll.Application.Abstractions;
using HrPayroll.Application.Common;
using HrPayroll.Domain.Entities;
using HrPayroll.Domain.Enums;
using HrPayroll.Infrastructure;
using HrPayroll.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO.Compression;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<CreateTenantRequestValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebDev", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "http://127.0.0.1:4200",
                "http://localhost:4201",
                "http://127.0.0.1:4201",
                "http://localhost:4202",
                "http://127.0.0.1:4202")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

await IdentitySeeder.SeedRolesAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

QuestPDF.Settings.License = LicenseType.Community;

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseCors("WebDev");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditLogMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");

api.MapPost("/tenants", async (
    CreateTenantRequest request,
    IApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    var slug = request.Slug.Trim().ToLowerInvariant();
    var tenantExists = await dbContext.Tenants.AnyAsync(x => x.Slug == slug, cancellationToken);
    if (tenantExists)
    {
        return Results.BadRequest(new { error = "Tenant slug already exists." });
    }

    var tenant = new Tenant
    {
        Name = request.TenantName.Trim(),
        Slug = slug,
        IsActive = true
    };

    dbContext.AddEntity(tenant);
    await dbContext.SaveChangesAsync(cancellationToken);

    var company = new CompanyProfile
    {
        TenantId = tenant.Id,
        LegalName = request.CompanyLegalName.Trim(),
        CurrencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "SAR" : request.CurrencyCode.Trim().ToUpperInvariant(),
        DefaultPayDay = request.DefaultPayDay,
        EosFirstFiveYearsMonthFactor = 0.5m,
        EosAfterFiveYearsMonthFactor = 1.0m
    };

    dbContext.AddEntity(company);

    var owner = new ApplicationUser
    {
        TenantId = tenant.Id,
        FirstName = request.OwnerFirstName.Trim(),
        LastName = request.OwnerLastName.Trim(),
        Email = request.OwnerEmail.Trim(),
        UserName = request.OwnerEmail.Trim(),
        NormalizedEmail = request.OwnerEmail.Trim().ToUpperInvariant(),
        NormalizedUserName = request.OwnerEmail.Trim().ToUpperInvariant(),
        EmailConfirmed = true
    };

    var createOwnerResult = await userManager.CreateAsync(owner, request.OwnerPassword);
    if (!createOwnerResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Failed to create owner user.", details = createOwnerResult.Errors.Select(x => x.Description) });
    }

    await userManager.AddToRoleAsync(owner, RoleNames.Owner);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/tenants/{tenant.Id}", new
    {
        tenant.Id,
        tenant.Name,
        tenant.Slug,
        OwnerEmail = owner.Email
    });
})
    .AddEndpointFilter<ValidationFilter<CreateTenantRequest>>()
    .AllowAnonymous();

api.MapPost("/auth/login", async (
    LoginRequest request,
    IApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator tokenGenerator,
    CancellationToken cancellationToken) =>
{
    Guid tenantId;
    if (request.TenantId.HasValue && request.TenantId.Value != Guid.Empty)
    {
        tenantId = request.TenantId.Value;
    }
    else if (!string.IsNullOrWhiteSpace(request.TenantSlug))
    {
        var slug = request.TenantSlug.Trim().ToLowerInvariant();
        var tenant = await dbContext.Tenants
            .FirstOrDefaultAsync(x => x.Slug == slug && x.IsActive, cancellationToken);

        if (tenant is null)
        {
            return Results.Unauthorized();
        }

        tenantId = tenant.Id;
    }
    else
    {
        return Results.Unauthorized();
    }

    var normalizedEmail = request.Email.Trim().ToUpperInvariant();

    var user = await userManager.Users
        .FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail && x.TenantId == tenantId, cancellationToken);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    var validPassword = await userManager.CheckPasswordAsync(user, request.Password);
    if (!validPassword)
    {
        return Results.Unauthorized();
    }

    var roles = await userManager.GetRolesAsync(user);
    var token = tokenGenerator.GenerateToken(user.Id, user.Email ?? string.Empty, user.TenantId, roles.ToArray());

    return Results.Ok(new
    {
        accessToken = token,
        user = new
        {
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.TenantId,
            Roles = roles
        }
    });
})
    .AddEndpointFilter<ValidationFilter<LoginRequest>>()
    .AllowAnonymous();

api.MapGet("/company-profile", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    ITenantContext tenantContext,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (profile is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(profile);
});

api.MapPut("/company-profile", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    UpdateCompanyProfileRequest request,
    ITenantContext tenantContext,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (profile is null)
    {
        return Results.NotFound();
    }

    profile.LegalName = request.LegalName.Trim();
    profile.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
    profile.DefaultPayDay = request.DefaultPayDay;
    profile.EosFirstFiveYearsMonthFactor = request.EosFirstFiveYearsMonthFactor;
    profile.EosAfterFiveYearsMonthFactor = request.EosAfterFiveYearsMonthFactor;
    profile.WpsCompanyBankName = (request.WpsCompanyBankName ?? string.Empty).Trim();
    profile.WpsCompanyBankCode = (request.WpsCompanyBankCode ?? string.Empty).Trim();
    profile.WpsCompanyIban = (request.WpsCompanyIban ?? string.Empty).Trim().ToUpperInvariant();
    profile.ComplianceDigestEnabled = request.ComplianceDigestEnabled;
    profile.ComplianceDigestEmail = (request.ComplianceDigestEmail ?? string.Empty).Trim();
    profile.ComplianceDigestFrequency = string.IsNullOrWhiteSpace(request.ComplianceDigestFrequency) ? "Weekly" : request.ComplianceDigestFrequency.Trim();
    profile.ComplianceDigestHourUtc = Math.Clamp(request.ComplianceDigestHourUtc, 0, 23);

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(profile);
})
    .AddEndpointFilter<ValidationFilter<UpdateCompanyProfileRequest>>();

api.MapPost("/users", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    CreateUserRequest request,
    ITenantContext tenantContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    if (!RoleNames.All.Contains(request.Role, StringComparer.Ordinal))
    {
        return Results.BadRequest(new { error = "Invalid role." });
    }

    var email = request.Email.Trim();
    var normalizedEmail = email.ToUpperInvariant();

    var exists = await userManager.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail && x.TenantId == tenantContext.TenantId, cancellationToken);
    if (exists)
    {
        return Results.BadRequest(new { error = "User already exists in this tenant." });
    }

    var user = new ApplicationUser
    {
        TenantId = tenantContext.TenantId,
        FirstName = request.FirstName.Trim(),
        LastName = request.LastName.Trim(),
        Email = email,
        UserName = email,
        NormalizedEmail = normalizedEmail,
        NormalizedUserName = normalizedEmail,
        EmailConfirmed = true
    };

    var createResult = await userManager.CreateAsync(user, request.Password);
    if (!createResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Failed to create user.", details = createResult.Errors.Select(x => x.Description) });
    }

    await userManager.AddToRoleAsync(user, request.Role);

    return Results.Created($"/api/users/{user.Id}", new
    {
        user.Id,
        user.Email,
        user.FirstName,
        user.LastName,
        user.TenantId,
        request.Role
    });
})
    .AddEndpointFilter<ValidationFilter<CreateUserRequest>>();

api.MapGet("/users", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    int? page,
    int? pageSize,
    string? search,
    ITenantContext tenantContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var safePage = Math.Max(1, page ?? 1);
    var safePageSize = Math.Clamp(pageSize ?? 20, 1, 200);
    search = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();

    var query = userManager.Users
        .Where(x => x.TenantId == tenantContext.TenantId);

    if (search is not null)
    {
        query = query.Where(x =>
            (((x.FirstName ?? "") + " " + (x.LastName ?? "")).ToLower().Contains(search)) ||
            ((x.Email ?? "").ToLower().Contains(search)));
    }

    var total = await query.CountAsync(cancellationToken);

    var users = await query
        .OrderBy(x => x.FirstName)
        .ThenBy(x => x.LastName)
        .Skip((safePage - 1) * safePageSize)
        .Take(safePageSize)
        .Select(x => new
        {
            x.Id,
            x.FirstName,
            x.LastName,
            x.Email,
            x.TenantId
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(new { items = users, total, page = safePage, pageSize = safePageSize });
});

api.MapGet("/employees", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] (
    int? page,
    int? pageSize,
    string? search,
    IApplicationDbContext dbContext) =>
{
    var safePage = Math.Max(1, page ?? 1);
    var safePageSize = Math.Clamp(pageSize ?? 20, 1, 200);
    search = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();

    var query = dbContext.Employees.AsQueryable();
    if (search is not null)
    {
        query = query.Where(x =>
            ((x.FirstName + " " + x.LastName).ToLower().Contains(search)) ||
            x.Email.ToLower().Contains(search) ||
            x.JobTitle.ToLower().Contains(search));
    }

    var total = query.Count();

    var employees = query
        .OrderBy(x => x.FirstName)
        .ThenBy(x => x.LastName)
        .Skip((safePage - 1) * safePageSize)
        .Take(safePageSize)
        .Select(x => new
        {
            x.Id,
            x.StartDate,
            x.FirstName,
            x.LastName,
            x.Email,
            x.JobTitle,
            x.BaseSalary,
            x.IsSaudiNational,
            x.IsGosiEligible,
            x.GosiBasicWage,
            x.GosiHousingAllowance,
            x.EmployeeNumber,
            x.BankName,
            x.BankIban,
            x.IqamaNumber,
            x.IqamaExpiryDate,
            x.WorkPermitExpiryDate,
            x.TenantId
        })
        .ToList();

    return Results.Ok(new { items = employees, total, page = safePage, pageSize = safePageSize });
});

api.MapPost("/employees", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    CreateEmployeeRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var employee = new Employee
    {
        StartDate = request.StartDate,
        FirstName = request.FirstName.Trim(),
        LastName = request.LastName.Trim(),
        Email = request.Email.Trim(),
        JobTitle = request.JobTitle.Trim(),
        BaseSalary = request.BaseSalary,
        IsSaudiNational = request.IsSaudiNational,
        IsGosiEligible = request.IsGosiEligible,
        GosiBasicWage = request.IsGosiEligible ? request.GosiBasicWage : 0m,
        GosiHousingAllowance = request.IsGosiEligible ? request.GosiHousingAllowance : 0m,
        EmployeeNumber = (request.EmployeeNumber ?? string.Empty).Trim(),
        BankName = (request.BankName ?? string.Empty).Trim(),
        BankIban = (request.BankIban ?? string.Empty).Trim().ToUpperInvariant(),
        IqamaNumber = (request.IqamaNumber ?? string.Empty).Trim(),
        IqamaExpiryDate = request.IqamaExpiryDate,
        WorkPermitExpiryDate = request.WorkPermitExpiryDate
    };

    dbContext.AddEntity(employee);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/employees/{employee.Id}", employee);
})
    .AddEndpointFilter<ValidationFilter<CreateEmployeeRequest>>();

api.MapPost("/employees/{employeeId:guid}/eos-estimate", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid employeeId,
    EstimateEosRequest request,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(new { error = "Company profile not found." });
    }

    var terminationDate = request.TerminationDate ?? DateOnly.FromDateTime(dateTimeProvider.UtcNow.Date);
    if (terminationDate < employee.StartDate)
    {
        return Results.BadRequest(new { error = "Termination date cannot be before employee start date." });
    }

    var serviceDays = terminationDate.DayNumber - employee.StartDate.DayNumber + 1;
    var serviceYears = Math.Round(serviceDays / 365m, 4);
    var firstYears = Math.Min(serviceYears, 5m);
    var remainingYears = Math.Max(0m, serviceYears - 5m);
    var eosMonths = Math.Round(
        (firstYears * profile.EosFirstFiveYearsMonthFactor) +
        (remainingYears * profile.EosAfterFiveYearsMonthFactor),
        4);
    var eosAmount = Math.Round(eosMonths * employee.BaseSalary, 2);

    return Results.Ok(new
    {
        employee.Id,
        employee.StartDate,
        employee.BaseSalary,
        terminationDate,
        serviceDays,
        serviceYears,
        firstYears,
        remainingYears,
        profile.EosFirstFiveYearsMonthFactor,
        profile.EosAfterFiveYearsMonthFactor,
        eosMonths,
        eosAmount,
        currencyCode = profile.CurrencyCode
    });
})
    .AddEndpointFilter<ValidationFilter<EstimateEosRequest>>();

api.MapPost("/employees/{employeeId:guid}/final-settlement/estimate", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid employeeId,
    FinalSettlementEstimateRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(new { error = "Company profile not found." });
    }

    if (request.TerminationDate < employee.StartDate)
    {
        return Results.BadRequest(new { error = "Termination date cannot be before employee start date." });
    }

    var targetYear = request.Year ?? request.TerminationDate.Year;
    var targetMonth = request.Month ?? request.TerminationDate.Month;
    var periodStart = new DateOnly(targetYear, targetMonth, 1);
    var periodEnd = periodStart.AddMonths(1).AddDays(-1);
    if (request.TerminationDate < periodEnd)
    {
        periodEnd = request.TerminationDate;
    }

    var manualDeductions = await dbContext.PayrollAdjustments
        .Where(x =>
            x.EmployeeId == employee.Id &&
            x.Year == targetYear &&
            x.Month == targetMonth &&
            x.Type == PayrollAdjustmentType.Deduction)
        .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

    var unpaidLeaves = await dbContext.LeaveRequests
        .Where(x =>
            x.EmployeeId == employee.Id &&
            x.LeaveType == LeaveType.Unpaid &&
            x.Status == LeaveRequestStatus.Approved &&
            x.StartDate <= periodEnd &&
            x.EndDate >= periodStart)
        .ToListAsync(cancellationToken);

    var unpaidLeaveDays = unpaidLeaves.Sum(x =>
    {
        var overlapStart = x.StartDate > periodStart ? x.StartDate : periodStart;
        var overlapEnd = x.EndDate < periodEnd ? x.EndDate : periodEnd;
        return overlapEnd < overlapStart ? 0 : overlapEnd.DayNumber - overlapStart.DayNumber + 1;
    });

    var dailyRate = employee.BaseSalary / 30m;
    var unpaidLeaveDeduction = Math.Round(unpaidLeaveDays * dailyRate, 2);

    var serviceDays = request.TerminationDate.DayNumber - employee.StartDate.DayNumber + 1;
    var serviceYears = Math.Round(serviceDays / 365m, 4);
    var firstYears = Math.Min(serviceYears, 5m);
    var remainingYears = Math.Max(0m, serviceYears - 5m);
    var eosMonths = Math.Round(
        (firstYears * profile.EosFirstFiveYearsMonthFactor) +
        (remainingYears * profile.EosAfterFiveYearsMonthFactor),
        4);
    var eosAmount = Math.Round(eosMonths * employee.BaseSalary, 2);

    var additionalManualDeduction = Math.Round(request.AdditionalManualDeduction, 2);
    var totalDeductions = Math.Round(manualDeductions + additionalManualDeduction + unpaidLeaveDeduction, 2);
    var netSettlement = Math.Round(eosAmount - totalDeductions, 2);

    return Results.Ok(new
    {
        employee.Id,
        EmployeeName = $"{employee.FirstName} {employee.LastName}",
        employee.StartDate,
        request.TerminationDate,
        PeriodYear = targetYear,
        PeriodMonth = targetMonth,
        employee.BaseSalary,
        serviceDays,
        serviceYears,
        eosMonths,
        eosAmount,
        unpaidLeaveDays,
        unpaidLeaveDeduction,
        manualDeductionsFromPayroll = manualDeductions,
        additionalManualDeduction,
        totalDeductions,
        netSettlement,
        request.Notes,
        currencyCode = profile.CurrencyCode
    });
})
    .AddEndpointFilter<ValidationFilter<FinalSettlementEstimateRequest>>();

api.MapPost("/employees/{employeeId:guid}/final-settlement/export-csv", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid employeeId,
    FinalSettlementEstimateRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(new { error = "Company profile not found." });
    }

    if (request.TerminationDate < employee.StartDate)
    {
        return Results.BadRequest(new { error = "Termination date cannot be before employee start date." });
    }

    var targetYear = request.Year ?? request.TerminationDate.Year;
    var targetMonth = request.Month ?? request.TerminationDate.Month;
    var periodStart = new DateOnly(targetYear, targetMonth, 1);
    var periodEnd = periodStart.AddMonths(1).AddDays(-1);
    if (request.TerminationDate < periodEnd)
    {
        periodEnd = request.TerminationDate;
    }

    var manualDeductions = await dbContext.PayrollAdjustments
        .Where(x =>
            x.EmployeeId == employee.Id &&
            x.Year == targetYear &&
            x.Month == targetMonth &&
            x.Type == PayrollAdjustmentType.Deduction)
        .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

    var unpaidLeaves = await dbContext.LeaveRequests
        .Where(x =>
            x.EmployeeId == employee.Id &&
            x.LeaveType == LeaveType.Unpaid &&
            x.Status == LeaveRequestStatus.Approved &&
            x.StartDate <= periodEnd &&
            x.EndDate >= periodStart)
        .ToListAsync(cancellationToken);

    var unpaidLeaveDays = unpaidLeaves.Sum(x =>
    {
        var overlapStart = x.StartDate > periodStart ? x.StartDate : periodStart;
        var overlapEnd = x.EndDate < periodEnd ? x.EndDate : periodEnd;
        return overlapEnd < overlapStart ? 0 : overlapEnd.DayNumber - overlapStart.DayNumber + 1;
    });

    var dailyRate = employee.BaseSalary / 30m;
    var unpaidLeaveDeduction = Math.Round(unpaidLeaveDays * dailyRate, 2);

    var serviceDays = request.TerminationDate.DayNumber - employee.StartDate.DayNumber + 1;
    var serviceYears = Math.Round(serviceDays / 365m, 4);
    var firstYears = Math.Min(serviceYears, 5m);
    var remainingYears = Math.Max(0m, serviceYears - 5m);
    var eosMonths = Math.Round(
        (firstYears * profile.EosFirstFiveYearsMonthFactor) +
        (remainingYears * profile.EosAfterFiveYearsMonthFactor),
        4);
    var eosAmount = Math.Round(eosMonths * employee.BaseSalary, 2);

    var additionalManualDeduction = Math.Round(request.AdditionalManualDeduction, 2);
    var totalDeductions = Math.Round(manualDeductions + additionalManualDeduction + unpaidLeaveDeduction, 2);
    var netSettlement = Math.Round(eosAmount - totalDeductions, 2);

    var csv = new StringBuilder();
    csv.AppendLine("Final Settlement Statement");
    csv.AppendLine($"Employee,{employee.FirstName} {employee.LastName}");
    csv.AppendLine($"Start Date,{employee.StartDate:yyyy-MM-dd}");
    csv.AppendLine($"Termination Date,{request.TerminationDate:yyyy-MM-dd}");
    csv.AppendLine($"Period,{targetYear}-{targetMonth:00}");
    csv.AppendLine($"Currency,{profile.CurrencyCode}");
    csv.AppendLine();
    csv.AppendLine("Item,Amount");
    csv.AppendLine($"EOS Amount,{eosAmount:F2}");
    csv.AppendLine($"Unpaid Leave Deduction,{unpaidLeaveDeduction:F2}");
    csv.AppendLine($"Manual Deductions (Payroll),{manualDeductions:F2}");
    csv.AppendLine($"Additional Manual Deduction,{additionalManualDeduction:F2}");
    csv.AppendLine($"Total Deductions,{totalDeductions:F2}");
    csv.AppendLine($"Net Final Settlement,{netSettlement:F2}");
    csv.AppendLine();
    csv.AppendLine($"Service Days,{serviceDays}");
    csv.AppendLine($"Service Years,{serviceYears:F4}");
    csv.AppendLine($"EOS Months,{eosMonths:F4}");
    csv.AppendLine($"Unpaid Leave Days,{unpaidLeaveDays}");
    csv.AppendLine($"Notes,{(request.Notes ?? string.Empty).Replace(',', ';')}");

    var fileName = $"final-settlement-{employee.FirstName}-{employee.LastName}-{request.TerminationDate:yyyyMMdd}.csv".Replace(" ", "-");
    return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
})
    .AddEndpointFilter<ValidationFilter<FinalSettlementEstimateRequest>>();

api.MapPost("/employees/{employeeId:guid}/final-settlement/export-pdf", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid employeeId,
    FinalSettlementEstimateRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(new { error = "Company profile not found." });
    }

    if (request.TerminationDate < employee.StartDate)
    {
        return Results.BadRequest(new { error = "Termination date cannot be before employee start date." });
    }

    var targetYear = request.Year ?? request.TerminationDate.Year;
    var targetMonth = request.Month ?? request.TerminationDate.Month;
    var periodStart = new DateOnly(targetYear, targetMonth, 1);
    var periodEnd = periodStart.AddMonths(1).AddDays(-1);
    if (request.TerminationDate < periodEnd)
    {
        periodEnd = request.TerminationDate;
    }

    var manualDeductions = await dbContext.PayrollAdjustments
        .Where(x =>
            x.EmployeeId == employee.Id &&
            x.Year == targetYear &&
            x.Month == targetMonth &&
            x.Type == PayrollAdjustmentType.Deduction)
        .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

    var unpaidLeaves = await dbContext.LeaveRequests
        .Where(x =>
            x.EmployeeId == employee.Id &&
            x.LeaveType == LeaveType.Unpaid &&
            x.Status == LeaveRequestStatus.Approved &&
            x.StartDate <= periodEnd &&
            x.EndDate >= periodStart)
        .ToListAsync(cancellationToken);

    var unpaidLeaveDays = unpaidLeaves.Sum(x =>
    {
        var overlapStart = x.StartDate > periodStart ? x.StartDate : periodStart;
        var overlapEnd = x.EndDate < periodEnd ? x.EndDate : periodEnd;
        return overlapEnd < overlapStart ? 0 : overlapEnd.DayNumber - overlapStart.DayNumber + 1;
    });

    var dailyRate = employee.BaseSalary / 30m;
    var unpaidLeaveDeduction = Math.Round(unpaidLeaveDays * dailyRate, 2);

    var serviceDays = request.TerminationDate.DayNumber - employee.StartDate.DayNumber + 1;
    var serviceYears = Math.Round(serviceDays / 365m, 4);
    var firstYears = Math.Min(serviceYears, 5m);
    var remainingYears = Math.Max(0m, serviceYears - 5m);
    var eosMonths = Math.Round(
        (firstYears * profile.EosFirstFiveYearsMonthFactor) +
        (remainingYears * profile.EosAfterFiveYearsMonthFactor),
        4);
    var eosAmount = Math.Round(eosMonths * employee.BaseSalary, 2);

    var additionalManualDeduction = Math.Round(request.AdditionalManualDeduction, 2);
    var totalDeductions = Math.Round(manualDeductions + additionalManualDeduction + unpaidLeaveDeduction, 2);
    var netSettlement = Math.Round(eosAmount - totalDeductions, 2);

    var pdf = Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Margin(28);
            page.Size(PageSizes.A4);
            page.Content().Column(col =>
            {
                col.Spacing(6);
                col.Item().Text($"{profile.LegalName}").FontSize(18).SemiBold();
                col.Item().Text("Final Settlement Statement / بيان التسوية النهائية").FontSize(13).SemiBold();
                col.Item().Text($"Employee / الموظف: {employee.FirstName} {employee.LastName}");
                col.Item().Text($"Start Date / تاريخ المباشرة: {employee.StartDate:yyyy-MM-dd}");
                col.Item().Text($"Termination Date / تاريخ نهاية الخدمة: {request.TerminationDate:yyyy-MM-dd}");
                col.Item().Text($"Period / الفترة: {targetYear}-{targetMonth:00}");
                col.Item().Text($"Currency / العملة: {profile.CurrencyCode}");
                col.Item().PaddingVertical(8).LineHorizontal(1);
                col.Item().Text($"EOS Amount / مكافأة نهاية الخدمة: {eosAmount:F2}");
                col.Item().Text($"Unpaid Leave Deduction / خصم إجازة غير مدفوعة: {unpaidLeaveDeduction:F2}");
                col.Item().Text($"Manual Deductions (Payroll) / خصومات الرواتب: {manualDeductions:F2}");
                col.Item().Text($"Additional Manual Deduction / خصم إضافي: {additionalManualDeduction:F2}");
                col.Item().Text($"Total Deductions / إجمالي الخصومات: {totalDeductions:F2}");
                col.Item().PaddingVertical(8).LineHorizontal(1);
                col.Item().Text($"Net Final Settlement / صافي التسوية النهائية: {netSettlement:F2} {profile.CurrencyCode}").FontSize(14).Bold();
                col.Item().PaddingTop(8).Text($"Service Years / سنوات الخدمة: {serviceYears:F4}");
                col.Item().Text($"EOS Months / أشهر المكافأة: {eosMonths:F4}");
                col.Item().Text($"Unpaid Leave Days / أيام الإجازة غير المدفوعة: {unpaidLeaveDays}");
                if (!string.IsNullOrWhiteSpace(request.Notes))
                {
                    col.Item().PaddingTop(6).Text($"Notes / ملاحظات: {request.Notes}");
                }
            });
        });
    }).GeneratePdf();

    var fileName = $"final-settlement-{employee.FirstName}-{employee.LastName}-{request.TerminationDate:yyyyMMdd}.pdf".Replace(" ", "-");
    return Results.File(pdf, "application/pdf", fileName);
})
    .AddEndpointFilter<ValidationFilter<FinalSettlementEstimateRequest>>();

api.MapPost("/employees/{employeeId:guid}/final-settlement/exports/pdf", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid employeeId,
    FinalSettlementEstimateRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    if (request.TerminationDate < employee.StartDate)
    {
        return Results.BadRequest(new { error = "Termination date cannot be before employee start date." });
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var metadataJson = JsonSerializer.Serialize(new
    {
        request.TerminationDate,
        request.Year,
        request.Month,
        request.AdditionalManualDeduction,
        request.Notes
    });

    var exportJob = new ExportArtifact
    {
        PayrollRunId = Guid.Empty,
        EmployeeId = employeeId,
        ArtifactType = "FinalSettlementPdf",
        MetadataJson = metadataJson,
        Status = ExportArtifactStatus.Pending,
        FileName = $"final-settlement-{employee.FirstName}-{employee.LastName}-{request.TerminationDate:yyyyMMdd}.pdf".Replace(" ", "-"),
        ContentType = "application/pdf",
        CreatedByUserId = userId
    };

    dbContext.AddEntity(exportJob);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Accepted($"/api/final-settlement/exports/{exportJob.Id}", new
    {
        exportJob.Id,
        exportJob.Status
    });
})
    .AddEndpointFilter<ValidationFilter<FinalSettlementEstimateRequest>>();

api.MapGet("/employees/{employeeId:guid}/final-settlement/exports", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] (
    Guid employeeId,
    IApplicationDbContext dbContext) =>
{
    var artifacts = dbContext.ExportArtifacts
        .Where(x => x.EmployeeId == employeeId && x.ArtifactType == "FinalSettlementPdf")
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(50)
        .Select(x => new
        {
            x.Id,
            x.EmployeeId,
            x.ArtifactType,
            x.FileName,
            x.ContentType,
            x.Status,
            x.SizeBytes,
            x.ErrorMessage,
            x.CreatedAtUtc,
            x.CompletedAtUtc
        })
        .ToList();

    return Results.Ok(artifacts);
});

api.MapGet("/final-settlement/exports", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] (
    Guid? employeeId,
    int? status,
    int? take,
    IApplicationDbContext dbContext) =>
{
    var safeTake = Math.Clamp(take ?? 100, 1, 500);

    var query = from export in dbContext.ExportArtifacts
                join employee in dbContext.Employees on export.EmployeeId equals employee.Id into employeeJoin
                from employee in employeeJoin.DefaultIfEmpty()
                where export.ArtifactType == "FinalSettlementPdf"
                select new
                {
                    export.Id,
                    export.EmployeeId,
                    EmployeeName = employee != null ? employee.FirstName + " " + employee.LastName : null,
                    export.ArtifactType,
                    export.FileName,
                    export.ContentType,
                    export.Status,
                    export.SizeBytes,
                    export.ErrorMessage,
                    export.CreatedAtUtc,
                    export.CompletedAtUtc
                };

    if (employeeId.HasValue)
    {
        query = query.Where(x => x.EmployeeId == employeeId.Value);
    }

    if (status.HasValue)
    {
        if (status.Value is < 1 or > 4)
        {
            return Results.BadRequest(new { error = "Invalid export status filter." });
        }

        query = query.Where(x => x.Status == (ExportArtifactStatus)status.Value);
    }

    var rows = query
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(safeTake)
        .ToList();

    return Results.Ok(rows);
});

api.MapGet("/employees/{employeeId:guid}/final-settlement/exports/zip", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid employeeId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    var completedExports = await dbContext.ExportArtifacts
        .Where(x =>
            x.EmployeeId == employeeId &&
            x.ArtifactType == "FinalSettlementPdf" &&
            x.Status == ExportArtifactStatus.Completed &&
            x.FileData != null &&
            x.FileData.Length > 0)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(200)
        .ToListAsync(cancellationToken);

    if (completedExports.Count == 0)
    {
        return Results.BadRequest(new { error = "No completed final settlement PDF exports were found." });
    }

    await using var zipStream = new MemoryStream();
    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var export in completedExports)
        {
            var baseName = string.IsNullOrWhiteSpace(export.FileName)
                ? $"final-settlement-{export.Id}.pdf"
                : export.FileName;

            var safeName = baseName.Replace("/", "-").Replace("\\", "-");
            var entry = archive.CreateEntry(safeName, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync(export.FileData!, cancellationToken);
        }
    }

    zipStream.Position = 0;
    var employeeSlug = $"{employee.FirstName}-{employee.LastName}".Replace(" ", "-");
    var zipFileName = $"final-settlement-{employeeSlug}-exports.zip";
    return Results.File(zipStream.ToArray(), "application/zip", zipFileName);
});

api.MapGet("/final-settlement/exports/zip", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid? employeeId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var query = dbContext.ExportArtifacts
        .Where(x =>
            x.ArtifactType == "FinalSettlementPdf" &&
            x.Status == ExportArtifactStatus.Completed &&
            x.FileData != null &&
            x.FileData.Length > 0);

    if (employeeId.HasValue)
    {
        var employeeExists = await dbContext.Employees.AnyAsync(x => x.Id == employeeId.Value, cancellationToken);
        if (!employeeExists)
        {
            return Results.NotFound(new { error = "Employee not found." });
        }

        query = query.Where(x => x.EmployeeId == employeeId.Value);
    }

    var completedExports = await query
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(500)
        .ToListAsync(cancellationToken);

    if (completedExports.Count == 0)
    {
        return Results.BadRequest(new { error = "No completed final settlement PDF exports were found." });
    }

    await using var zipStream = new MemoryStream();
    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var export in completedExports)
        {
            var baseName = string.IsNullOrWhiteSpace(export.FileName)
                ? $"final-settlement-{export.Id}.pdf"
                : export.FileName;

            var safeName = baseName.Replace("/", "-").Replace("\\", "-");
            var entry = archive.CreateEntry(safeName, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync(export.FileData!, cancellationToken);
        }
    }

    zipStream.Position = 0;
    var zipFileName = employeeId.HasValue
        ? $"final-settlement-employee-{employeeId.Value}-exports.zip"
        : "final-settlement-all-exports.zip";
    return Results.File(zipStream.ToArray(), "application/zip", zipFileName);
});

api.MapGet("/final-settlement/exports/{exportId:guid}", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid exportId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var export = await dbContext.ExportArtifacts.FirstOrDefaultAsync(x => x.Id == exportId && x.ArtifactType == "FinalSettlementPdf", cancellationToken);
    if (export is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        export.Id,
        export.EmployeeId,
        export.ArtifactType,
        export.FileName,
        export.ContentType,
        export.Status,
        export.SizeBytes,
        export.ErrorMessage,
        export.CreatedAtUtc,
        export.CompletedAtUtc
    });
});

api.MapPost("/final-settlement/exports/{exportId:guid}/retry", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid exportId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var export = await dbContext.ExportArtifacts.FirstOrDefaultAsync(x => x.Id == exportId && x.ArtifactType == "FinalSettlementPdf", cancellationToken);
    if (export is null)
    {
        return Results.NotFound();
    }

    if (export.Status != ExportArtifactStatus.Failed)
    {
        return Results.BadRequest(new { error = "Only failed exports can be retried." });
    }

    export.Status = ExportArtifactStatus.Pending;
    export.ErrorMessage = null;
    export.FileData = null;
    export.SizeBytes = 0;
    export.CompletedAtUtc = null;

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        export.Id,
        export.Status
    });
});

api.MapGet("/final-settlement/exports/{exportId:guid}/download", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid exportId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var export = await dbContext.ExportArtifacts.FirstOrDefaultAsync(x => x.Id == exportId && x.ArtifactType == "FinalSettlementPdf", cancellationToken);
    if (export is null)
    {
        return Results.NotFound();
    }

    if (export.Status != ExportArtifactStatus.Completed || export.FileData is null || export.FileData.Length == 0)
    {
        return Results.BadRequest(new { error = "Export file is not ready." });
    }

    return Results.File(export.FileData, export.ContentType, export.FileName);
});

api.MapGet("/attendance-inputs", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] (
    int year,
    int month,
    int? page,
    int? pageSize,
    string? search,
    IApplicationDbContext dbContext) =>
{
    var safePage = Math.Max(1, page ?? 1);
    var safePageSize = Math.Clamp(pageSize ?? 20, 1, 200);
    search = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();

    var query = from attendance in dbContext.AttendanceInputs
                join employee in dbContext.Employees on attendance.EmployeeId equals employee.Id
                where attendance.Year == year && attendance.Month == month
                select new
                {
                    attendance.Id,
                    attendance.EmployeeId,
                    EmployeeName = employee.FirstName + " " + employee.LastName,
                    attendance.Year,
                    attendance.Month,
                    attendance.DaysPresent,
                    attendance.DaysAbsent,
                    attendance.OvertimeHours
                };

    if (search is not null)
    {
        query = query.Where(x => x.EmployeeName.ToLower().Contains(search));
    }

    var total = query.Count();

    var rows = query
        .OrderBy(x => x.EmployeeName)
        .Skip((safePage - 1) * safePageSize)
        .Take(safePageSize)
        .ToList();

    return Results.Ok(new { items = rows, total, page = safePage, pageSize = safePageSize });
});

api.MapGet("/compliance/expiries", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] (
    int? days,
    bool? includeSaudi,
    int? take,
    IApplicationDbContext dbContext) =>
{
    var maxDays = Math.Clamp(days ?? 60, 1, 365);
    var maxTake = Math.Clamp(take ?? 200, 1, 1000);
    var includeSaudiEmployees = includeSaudi ?? false;

    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

    var employees = dbContext.Employees
        .Where(x => includeSaudiEmployees || !x.IsSaudiNational)
        .Select(x => new
        {
            x.Id,
            EmployeeName = x.FirstName + " " + x.LastName,
            x.IsSaudiNational,
            x.IqamaNumber,
            x.IqamaExpiryDate,
            x.WorkPermitExpiryDate
        })
        .ToList();

    var alerts = new List<(Guid Id, string EmployeeName, bool IsSaudiNational, string DocumentType, string DocumentNumber, DateOnly ExpiryDate, int DaysLeft)>();
    foreach (var employee in employees)
    {
        if (employee.IqamaExpiryDate.HasValue)
        {
            var daysLeft = employee.IqamaExpiryDate.Value.DayNumber - today.DayNumber;
            if (daysLeft >= 0 && daysLeft <= maxDays)
            {
                alerts.Add((
                    employee.Id,
                    employee.EmployeeName,
                    employee.IsSaudiNational,
                    "Iqama",
                    employee.IqamaNumber,
                    employee.IqamaExpiryDate.Value,
                    daysLeft
                ));
            }
        }

        if (employee.WorkPermitExpiryDate.HasValue)
        {
            var daysLeft = employee.WorkPermitExpiryDate.Value.DayNumber - today.DayNumber;
            if (daysLeft >= 0 && daysLeft <= maxDays)
            {
                alerts.Add((
                    employee.Id,
                    employee.EmployeeName,
                    employee.IsSaudiNational,
                    "WorkPermit",
                    employee.IqamaNumber,
                    employee.WorkPermitExpiryDate.Value,
                    daysLeft
                ));
            }
        }
    }

    var items = alerts
        .OrderBy(x => x.DaysLeft)
        .ThenBy(x => x.EmployeeName)
        .Take(maxTake)
        .Select(x => new
        {
            x.Id,
            x.EmployeeName,
            x.IsSaudiNational,
            documentType = x.DocumentType,
            documentNumber = x.DocumentNumber,
            expiryDate = x.ExpiryDate,
            daysLeft = x.DaysLeft
        })
        .ToList();

    var critical = alerts.Count(x => x.DaysLeft <= 7);
    var warning = alerts.Count(x => x.DaysLeft > 7 && x.DaysLeft <= 30);
    var notice = alerts.Count(x => x.DaysLeft > 30);

    return Results.Ok(new
    {
        days = maxDays,
        total = items.Count,
        critical,
        warning,
        notice,
        items
    });
});

api.MapGet("/compliance/alerts", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] (
    bool? resolved,
    string? severity,
    int? take,
    IApplicationDbContext dbContext) =>
{
    var includeResolved = resolved ?? false;
    var safeTake = Math.Clamp(take ?? 100, 1, 500);
    var severityFilter = string.IsNullOrWhiteSpace(severity) ? null : severity.Trim();

    var query = dbContext.ComplianceAlerts.AsQueryable();

    if (!includeResolved)
    {
        query = query.Where(x => !x.IsResolved);
    }

    if (!string.IsNullOrWhiteSpace(severityFilter))
    {
        query = query.Where(x => x.Severity == severityFilter);
    }

    var items = query
        .OrderBy(x => x.IsResolved)
        .ThenBy(x => x.DaysLeft)
        .ThenBy(x => x.EmployeeName)
        .Take(safeTake)
        .Select(x => new
        {
            x.Id,
            x.EmployeeId,
            x.EmployeeName,
            x.IsSaudiNational,
            x.DocumentType,
            x.DocumentNumber,
            x.ExpiryDate,
            x.DaysLeft,
            x.Severity,
            x.IsResolved,
            x.ResolveReason,
            x.ResolvedAtUtc,
            x.LastDetectedAtUtc
        })
        .ToList();

    var openQuery = dbContext.ComplianceAlerts.Where(x => !x.IsResolved);
    var critical = openQuery.Count(x => x.Severity == "Critical");
    var warning = openQuery.Count(x => x.Severity == "Warning");
    var notice = openQuery.Count(x => x.Severity == "Notice");

    return Results.Ok(new
    {
        total = items.Count,
        critical,
        warning,
        notice,
        items
    });
});

api.MapPost("/compliance/alerts/{alertId:guid}/resolve", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid alertId,
    ResolveComplianceAlertRequest request,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var alert = await dbContext.ComplianceAlerts.FirstOrDefaultAsync(x => x.Id == alertId, cancellationToken);
    if (alert is null)
    {
        return Results.NotFound(new { error = "Compliance alert not found." });
    }

    if (alert.IsResolved)
    {
        return Results.Ok(new
        {
            alert.Id,
            alert.IsResolved,
            alert.ResolveReason,
            alert.ResolvedAtUtc
        });
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    alert.IsResolved = true;
    alert.ResolveReason = string.IsNullOrWhiteSpace(request.Reason) ? "ResolvedManually" : request.Reason.Trim();
    alert.ResolvedByUserId = userId;
    alert.ResolvedAtUtc = dateTimeProvider.UtcNow;

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        alert.Id,
        alert.IsResolved,
        alert.ResolveReason,
        alert.ResolvedAtUtc
    });
})
    .AddEndpointFilter<ValidationFilter<ResolveComplianceAlertRequest>>();

api.MapPost("/compliance/alerts/resolve-bulk", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    ResolveComplianceAlertsBulkRequest request,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var ids = request.AlertIds
        .Where(x => x != Guid.Empty)
        .Distinct()
        .Take(200)
        .ToArray();

    if (ids.Length == 0)
    {
        return Results.BadRequest(new { error = "No valid alert ids were provided." });
    }

    var alerts = await dbContext.ComplianceAlerts
        .Where(x => ids.Contains(x.Id))
        .ToListAsync(cancellationToken);

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var resolvedCount = 0;
    foreach (var alert in alerts.Where(x => !x.IsResolved))
    {
        alert.IsResolved = true;
        alert.ResolveReason = string.IsNullOrWhiteSpace(request.Reason) ? "ResolvedInBulk" : request.Reason.Trim();
        alert.ResolvedByUserId = userId;
        alert.ResolvedAtUtc = dateTimeProvider.UtcNow;
        resolvedCount++;
    }

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        requested = ids.Length,
        found = alerts.Count,
        resolved = resolvedCount
    });
})
    .AddEndpointFilter<ValidationFilter<ResolveComplianceAlertsBulkRequest>>();

api.MapGet("/compliance/score", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var score = await BuildComplianceScoreAsync(dbContext, cancellationToken);
    return Results.Ok(score);
});

api.MapGet("/compliance/score-history", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    int? days,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var safeDays = Math.Clamp(days ?? 60, 7, 365);
    var since = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-(safeDays - 1));

    var snapshots = await dbContext.ComplianceScoreSnapshots
        .Where(x => x.SnapshotDate >= since)
        .OrderBy(x => x.SnapshotDate)
        .Select(x => new ComplianceScoreHistoryItemResponse(
            x.SnapshotDate,
            x.Score,
            x.Grade,
            x.SaudizationPercent,
            x.WpsCompanyReady,
            x.EmployeesMissingPaymentData,
            x.CriticalAlerts,
            x.WarningAlerts,
            x.NoticeAlerts))
        .ToListAsync(cancellationToken);

    return Results.Ok(new
    {
        days = safeDays,
        items = snapshots
    });
});

api.MapPost("/compliance/digest/send-now", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    IApplicationDbContext dbContext,
    IConfiguration configuration,
    IDateTimeProvider dateTimeProvider,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(new { error = "Company profile not found." });
    }

    if (string.IsNullOrWhiteSpace(profile.ComplianceDigestEmail))
    {
        return Results.BadRequest(new { error = "Compliance digest email is not configured in company profile." });
    }

    var score = await BuildComplianceScoreAsync(dbContext, cancellationToken);
    var topAlerts = await dbContext.ComplianceAlerts
        .Where(x => !x.IsResolved)
        .OrderBy(x => x.DaysLeft)
        .ThenBy(x => x.EmployeeName)
        .Take(10)
        .Select(x => new ComplianceDigestAlertItemResponse(
            x.EmployeeName,
            x.DocumentType,
            x.DaysLeft,
            x.Severity,
            x.ExpiryDate))
        .ToListAsync(cancellationToken);

    var nowUtc = dateTimeProvider.UtcNow;
    var textBody = BuildComplianceDigestBodyText(profile.LegalName, nowUtc, score, topAlerts);
    var htmlBody = BuildComplianceDigestBodyHtml(profile.LegalName, nowUtc, score, topAlerts);
    var subject = $"Compliance Digest - {profile.LegalName} - {nowUtc:yyyy-MM-dd}";
    var sendResult = await TrySendComplianceDigestEmailAsync(
        configuration,
        profile.ComplianceDigestEmail,
        subject,
        textBody,
        htmlBody,
        cancellationToken);

    if (!sendResult.Sent)
    {
        dbContext.AddEntity(new ComplianceDigestDelivery
        {
            TenantId = profile.TenantId,
            RecipientEmail = profile.ComplianceDigestEmail,
            Subject = subject,
            TriggerType = "Manual",
            Frequency = string.IsNullOrWhiteSpace(profile.ComplianceDigestFrequency) ? "Weekly" : profile.ComplianceDigestFrequency,
            Status = "Failed",
            Simulated = false,
            ErrorMessage = sendResult.ErrorMessage ?? "Unknown error.",
            Score = score.Score,
            Grade = score.Grade,
            SentAtUtc = null
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Problem("Failed to send compliance digest email.", statusCode: 502);
    }

    if (sendResult.Simulated)
    {
        logger.LogInformation("Digest send-now simulated (SMTP not configured) for {ToEmail}. Body:\n{Body}", profile.ComplianceDigestEmail, textBody);
    }

    profile.LastComplianceDigestSentAtUtc = nowUtc;
    dbContext.AddEntity(new ComplianceDigestDelivery
    {
        TenantId = profile.TenantId,
        RecipientEmail = profile.ComplianceDigestEmail,
        Subject = subject,
        TriggerType = "Manual",
        Frequency = string.IsNullOrWhiteSpace(profile.ComplianceDigestFrequency) ? "Weekly" : profile.ComplianceDigestFrequency,
        Status = sendResult.Simulated ? "Simulated" : "Sent",
        Simulated = sendResult.Simulated,
        ErrorMessage = string.Empty,
        Score = score.Score,
        Grade = score.Grade,
        SentAtUtc = nowUtc
    });
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new
    {
        sent = true,
        simulated = sendResult.Simulated,
        to = profile.ComplianceDigestEmail,
        atUtc = nowUtc,
        score = score.Score,
        grade = score.Grade
    });
});

api.MapGet("/compliance/digest/logs", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] (
    string? status,
    string? triggerType,
    string? search,
    int? skip,
    int? take,
    IApplicationDbContext dbContext) =>
{
    var safeSkip = Math.Max(skip ?? 0, 0);
    var safeTake = Math.Clamp(take ?? 50, 1, 200);

    var query = dbContext.ComplianceDigestDeliveries.AsQueryable();

    if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
    {
        query = query.Where(x => x.Status == status);
    }

    if (!string.IsNullOrWhiteSpace(triggerType) && !string.Equals(triggerType, "All", StringComparison.OrdinalIgnoreCase))
    {
        query = query.Where(x => x.TriggerType == triggerType);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        var s = search.Trim().ToLowerInvariant();
        query = query.Where(x =>
            x.RecipientEmail.ToLower().Contains(s) ||
            x.Status.ToLower().Contains(s) ||
            x.TriggerType.ToLower().Contains(s) ||
            x.ErrorMessage.ToLower().Contains(s));
    }

    var total = query.Count();

    var rows = query
        .OrderByDescending(x => x.CreatedAtUtc)
        .Skip(safeSkip)
        .Take(safeTake)
        .Select(x => new
        {
            x.Id,
            x.RetryOfDeliveryId,
            x.RecipientEmail,
            x.Subject,
            x.TriggerType,
            x.Frequency,
            x.Status,
            x.Simulated,
            x.ErrorMessage,
            x.Score,
            x.Grade,
            x.SentAtUtc,
            x.CreatedAtUtc
        })
        .ToList();

    return Results.Ok(new
    {
        items = rows,
        total,
        skip = safeSkip,
        take = safeTake,
        hasMore = safeSkip + rows.Count < total
    });
});

api.MapPost("/compliance/digest/retry/{deliveryId:guid}", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid deliveryId,
    IApplicationDbContext dbContext,
    IConfiguration configuration,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var source = await dbContext.ComplianceDigestDeliveries.FirstOrDefaultAsync(x => x.Id == deliveryId, cancellationToken);
    if (source is null)
    {
        return Results.NotFound(new { error = "Digest delivery log not found." });
    }

    if (!string.Equals(source.Status, "Failed", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Only failed digest deliveries can be retried." });
    }

    var company = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (company is null)
    {
        return Results.NotFound(new { error = "Company profile not found." });
    }

    var score = await BuildComplianceScoreAsync(dbContext, cancellationToken);
    var topAlerts = await dbContext.ComplianceAlerts
        .Where(x => !x.IsResolved)
        .OrderBy(x => x.DaysLeft)
        .ThenBy(x => x.EmployeeName)
        .Take(10)
        .Select(x => new ComplianceDigestAlertItemResponse(
            x.EmployeeName,
            x.DocumentType,
            x.DaysLeft,
            x.Severity,
            x.ExpiryDate))
        .ToListAsync(cancellationToken);

    var nowUtc = dateTimeProvider.UtcNow;
    var subject = $"Compliance Digest Retry - {company.LegalName} - {nowUtc:yyyy-MM-dd}";
    var textBody = BuildComplianceDigestBodyText(company.LegalName, nowUtc, score, topAlerts);
    var htmlBody = BuildComplianceDigestBodyHtml(company.LegalName, nowUtc, score, topAlerts);
    var sendResult = await TrySendComplianceDigestEmailAsync(configuration, source.RecipientEmail, subject, textBody, htmlBody, cancellationToken);

    dbContext.AddEntity(new ComplianceDigestDelivery
    {
        TenantId = source.TenantId,
        RetryOfDeliveryId = source.Id,
        RecipientEmail = source.RecipientEmail,
        Subject = subject,
        TriggerType = "Retry",
        Frequency = source.Frequency,
        Status = sendResult.Sent ? (sendResult.Simulated ? "Simulated" : "Sent") : "Failed",
        Simulated = sendResult.Simulated,
        ErrorMessage = sendResult.Sent ? string.Empty : (sendResult.ErrorMessage ?? "Unknown error."),
        Score = score.Score,
        Grade = score.Grade,
        SentAtUtc = sendResult.Sent ? nowUtc : null
    });
    await dbContext.SaveChangesAsync(cancellationToken);

    if (!sendResult.Sent)
    {
        return Results.Problem("Retry failed to send compliance digest email.", statusCode: 502);
    }

    return Results.Ok(new
    {
        sent = true,
        simulated = sendResult.Simulated,
        to = source.RecipientEmail,
        atUtc = nowUtc
    });
});

api.MapPost("/compliance/ai-brief", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    ComplianceAiBriefRequest request,
    IApplicationDbContext dbContext,
    IComplianceAiService complianceAiService,
    CancellationToken cancellationToken) =>
{
    var score = await BuildComplianceScoreAsync(dbContext, cancellationToken);

    var aiInput = new ComplianceAiInput(
        Language: string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim(),
        Score: score.Score,
        Grade: score.Grade,
        SaudizationPercent: score.SaudizationPercent,
        WpsCompanyReady: score.WpsCompanyReady,
        EmployeesMissingPaymentData: score.EmployeesMissingPaymentData,
        CriticalAlerts: score.CriticalAlerts,
        WarningAlerts: score.WarningAlerts,
        NoticeAlerts: score.NoticeAlerts,
        Recommendations: score.Recommendations,
        UserPrompt: request.Prompt);

    var result = await complianceAiService.GenerateBriefAsync(aiInput, cancellationToken);

    return Results.Ok(new
    {
        score.Score,
        score.Grade,
        score.SaudizationPercent,
        score.CriticalAlerts,
        score.WarningAlerts,
        score.NoticeAlerts,
        provider = result.Provider,
        usedFallback = result.UsedFallback,
        brief = result.Text
    });
})
    .AddEndpointFilter<ValidationFilter<ComplianceAiBriefRequest>>();

api.MapPost("/attendance-inputs", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    UpsertAttendanceInputRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (request.Month is < 1 or > 12)
    {
        return Results.BadRequest(new { error = "Month must be between 1 and 12." });
    }

    if (request.Year is < 2000 or > 2100)
    {
        return Results.BadRequest(new { error = "Year is out of range." });
    }

    if (request.DaysPresent < 0 || request.DaysAbsent < 0 || request.OvertimeHours < 0)
    {
        return Results.BadRequest(new { error = "Attendance values cannot be negative." });
    }

    var employeeExists = await dbContext.Employees.AnyAsync(x => x.Id == request.EmployeeId, cancellationToken);
    if (!employeeExists)
    {
        return Results.BadRequest(new { error = "Employee not found in this tenant." });
    }

    var current = await dbContext.AttendanceInputs.FirstOrDefaultAsync(
        x => x.EmployeeId == request.EmployeeId && x.Year == request.Year && x.Month == request.Month,
        cancellationToken);

    if (current is null)
    {
        current = new AttendanceInput
        {
            EmployeeId = request.EmployeeId,
            Year = request.Year,
            Month = request.Month,
            DaysPresent = request.DaysPresent,
            DaysAbsent = request.DaysAbsent,
            OvertimeHours = request.OvertimeHours
        };
        dbContext.AddEntity(current);
    }
    else
    {
        current.DaysPresent = request.DaysPresent;
        current.DaysAbsent = request.DaysAbsent;
        current.OvertimeHours = request.OvertimeHours;
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(current);
})
    .AddEndpointFilter<ValidationFilter<UpsertAttendanceInputRequest>>();

api.MapGet("/leave/requests", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager + "," + RoleNames.Employee)] async (
    int? status,
    Guid? employeeId,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var canReview = httpContext.User.IsInRole(RoleNames.Owner) ||
                    httpContext.User.IsInRole(RoleNames.Admin) ||
                    httpContext.User.IsInRole(RoleNames.Hr) ||
                    httpContext.User.IsInRole(RoleNames.Manager);

    var query = dbContext.LeaveRequests.AsQueryable();

    if (status.HasValue)
    {
        if (status.Value is < 1 or > 3)
        {
            return Results.BadRequest(new { error = "Invalid leave request status." });
        }

        var parsedStatus = (LeaveRequestStatus)status.Value;
        query = query.Where(x => x.Status == parsedStatus);
    }

    if (canReview)
    {
        if (employeeId.HasValue)
        {
            query = query.Where(x => x.EmployeeId == employeeId.Value);
        }
    }
    else
    {
        var userEmail = httpContext.User.Claims
            .Where(x => x.Type is "email" or ClaimTypes.Email)
            .Select(x => x.Value)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Results.BadRequest(new { error = "Your account email was not found in token." });
        }

        var ownEmployee = await dbContext.Employees.FirstOrDefaultAsync(
            x => x.Email.ToLower() == userEmail.ToLower(),
            cancellationToken);

        if (ownEmployee is null)
        {
            return Results.BadRequest(new { error = "No employee profile linked to your account email." });
        }

        query = query.Where(x => x.EmployeeId == ownEmployee.Id);
    }

    var rows = await (from leaveRequest in query
                      join employee in dbContext.Employees on leaveRequest.EmployeeId equals employee.Id
                      orderby leaveRequest.CreatedAtUtc descending
                      select new
                      {
                          leaveRequest.Id,
                          leaveRequest.EmployeeId,
                          EmployeeName = employee.FirstName + " " + employee.LastName,
                          leaveRequest.LeaveType,
                          leaveRequest.StartDate,
                          leaveRequest.EndDate,
                          leaveRequest.TotalDays,
                          leaveRequest.Reason,
                          leaveRequest.Status,
                          leaveRequest.RejectionReason,
                          leaveRequest.ReviewedByUserId,
                          leaveRequest.ReviewedAtUtc,
                          leaveRequest.CreatedAtUtc
                      }).ToListAsync(cancellationToken);

    return Results.Ok(rows);
});

api.MapGet("/leave/balances", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager + "," + RoleNames.Employee)] async (
    int year,
    Guid? employeeId,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var canReview = httpContext.User.IsInRole(RoleNames.Owner) ||
                    httpContext.User.IsInRole(RoleNames.Admin) ||
                    httpContext.User.IsInRole(RoleNames.Hr) ||
                    httpContext.User.IsInRole(RoleNames.Manager);

    var query = dbContext.LeaveBalances.Where(x => x.Year == year);

    if (canReview)
    {
        if (employeeId.HasValue)
        {
            query = query.Where(x => x.EmployeeId == employeeId.Value);
        }
    }
    else
    {
        var userEmail = httpContext.User.Claims
            .Where(x => x.Type is "email" or ClaimTypes.Email)
            .Select(x => x.Value)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Results.BadRequest(new { error = "Your account email was not found in token." });
        }

        var ownEmployee = await dbContext.Employees.FirstOrDefaultAsync(
            x => x.Email.ToLower() == userEmail.ToLower(),
            cancellationToken);

        if (ownEmployee is null)
        {
            return Results.BadRequest(new { error = "No employee profile linked to your account email." });
        }

        query = query.Where(x => x.EmployeeId == ownEmployee.Id);
    }

    var rows = await (from balance in query
                      join employee in dbContext.Employees on balance.EmployeeId equals employee.Id
                      orderby employee.FirstName, employee.LastName, balance.LeaveType
                      select new
                      {
                          balance.Id,
                          balance.EmployeeId,
                          EmployeeName = employee.FirstName + " " + employee.LastName,
                          balance.Year,
                          balance.LeaveType,
                          balance.AllocatedDays,
                          balance.UsedDays,
                          RemainingDays = balance.AllocatedDays - balance.UsedDays
                      }).ToListAsync(cancellationToken);

    return Results.Ok(rows);
});

api.MapPost("/leave/requests", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager + "," + RoleNames.Employee)] async (
    CreateLeaveRequestRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var canReview = httpContext.User.IsInRole(RoleNames.Owner) ||
                    httpContext.User.IsInRole(RoleNames.Admin) ||
                    httpContext.User.IsInRole(RoleNames.Hr) ||
                    httpContext.User.IsInRole(RoleNames.Manager);

    Guid resolvedEmployeeId;
    if (canReview && request.EmployeeId.HasValue)
    {
        resolvedEmployeeId = request.EmployeeId.Value;
    }
    else
    {
        var userEmail = httpContext.User.Claims
            .Where(x => x.Type is "email" or ClaimTypes.Email)
            .Select(x => x.Value)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Results.BadRequest(new { error = "Your account email was not found in token." });
        }

        var ownEmployee = await dbContext.Employees.FirstOrDefaultAsync(
            x => x.Email.ToLower() == userEmail.ToLower(),
            cancellationToken);

        if (ownEmployee is null)
        {
            return Results.BadRequest(new { error = "No employee profile linked to your account email." });
        }

        resolvedEmployeeId = ownEmployee.Id;
    }

    var employeeExists = await dbContext.Employees.AnyAsync(x => x.Id == resolvedEmployeeId, cancellationToken);
    if (!employeeExists)
    {
        return Results.BadRequest(new { error = "Employee not found in this tenant." });
    }

    if (request.EndDate < request.StartDate)
    {
        return Results.BadRequest(new { error = "End date must be on or after start date." });
    }

    var hasOverlap = await dbContext.LeaveRequests.AnyAsync(
        x => x.EmployeeId == resolvedEmployeeId
             && x.Status != LeaveRequestStatus.Rejected
             && x.StartDate <= request.EndDate
             && x.EndDate >= request.StartDate,
        cancellationToken);

    if (hasOverlap)
    {
        return Results.BadRequest(new { error = "A pending or approved leave request overlaps this period." });
    }

    var totalDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;

    var leaveRequest = new LeaveRequest
    {
        EmployeeId = resolvedEmployeeId,
        LeaveType = request.LeaveType,
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        TotalDays = totalDays,
        Reason = request.Reason.Trim(),
        Status = LeaveRequestStatus.Pending
    };

    dbContext.AddEntity(leaveRequest);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/leave/requests/{leaveRequest.Id}", leaveRequest);
})
    .AddEndpointFilter<ValidationFilter<CreateLeaveRequestRequest>>();

api.MapPost("/leave/requests/{requestId:guid}/approve", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid requestId,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var leaveRequest = await dbContext.LeaveRequests.FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
    if (leaveRequest is null)
    {
        return Results.NotFound();
    }

    if (leaveRequest.Status != LeaveRequestStatus.Pending)
    {
        return Results.BadRequest(new { error = "Only pending requests can be approved." });
    }

    if (leaveRequest.LeaveType != LeaveType.Unpaid)
    {
        var year = leaveRequest.StartDate.Year;
        var balance = await dbContext.LeaveBalances.FirstOrDefaultAsync(
            x => x.EmployeeId == leaveRequest.EmployeeId && x.Year == year && x.LeaveType == leaveRequest.LeaveType,
            cancellationToken);

        if (balance is null)
        {
            var allocatedDays = leaveRequest.LeaveType switch
            {
                LeaveType.Annual => 21m,
                LeaveType.Sick => 10m,
                _ => 0m
            };

            balance = new LeaveBalance
            {
                EmployeeId = leaveRequest.EmployeeId,
                Year = year,
                LeaveType = leaveRequest.LeaveType,
                AllocatedDays = allocatedDays,
                UsedDays = 0
            };

            dbContext.AddEntity(balance);
        }

        if (balance.UsedDays + leaveRequest.TotalDays > balance.AllocatedDays)
        {
            return Results.BadRequest(new { error = "Insufficient leave balance for approval." });
        }

        balance.UsedDays += leaveRequest.TotalDays;
    }

    Guid? reviewedBy = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        reviewedBy = parsedUserId;
    }

    leaveRequest.Status = LeaveRequestStatus.Approved;
    leaveRequest.ReviewedByUserId = reviewedBy;
    leaveRequest.ReviewedAtUtc = dateTimeProvider.UtcNow;
    leaveRequest.RejectionReason = null;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { leaveRequest.Id, leaveRequest.Status });
});

api.MapPost("/leave/requests/{requestId:guid}/reject", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid requestId,
    RejectLeaveRequestRequest request,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var leaveRequest = await dbContext.LeaveRequests.FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
    if (leaveRequest is null)
    {
        return Results.NotFound();
    }

    if (leaveRequest.Status != LeaveRequestStatus.Pending)
    {
        return Results.BadRequest(new { error = "Only pending requests can be rejected." });
    }

    Guid? reviewedBy = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        reviewedBy = parsedUserId;
    }

    leaveRequest.Status = LeaveRequestStatus.Rejected;
    leaveRequest.ReviewedByUserId = reviewedBy;
    leaveRequest.ReviewedAtUtc = dateTimeProvider.UtcNow;
    leaveRequest.RejectionReason = request.RejectionReason.Trim();

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { leaveRequest.Id, leaveRequest.Status });
})
    .AddEndpointFilter<ValidationFilter<RejectLeaveRequestRequest>>();

api.MapGet("/payroll/periods", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] (IApplicationDbContext dbContext) =>
{
    var periods = dbContext.PayrollPeriods
        .OrderByDescending(x => x.Year)
        .ThenByDescending(x => x.Month)
        .Select(x => new
        {
            x.Id,
            x.Year,
            x.Month,
            x.Status,
            x.PeriodStartDate,
            x.PeriodEndDate
        })
        .ToList();

    return Results.Ok(periods);
});

api.MapPost("/payroll/periods", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    CreatePayrollPeriodRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (request.Month is < 1 or > 12)
    {
        return Results.BadRequest(new { error = "Month must be between 1 and 12." });
    }

    var exists = await dbContext.PayrollPeriods.AnyAsync(x => x.Year == request.Year && x.Month == request.Month, cancellationToken);
    if (exists)
    {
        return Results.BadRequest(new { error = "Payroll period already exists." });
    }

    var period = new PayrollPeriod
    {
        Year = request.Year,
        Month = request.Month,
        PeriodStartDate = request.PeriodStartDate,
        PeriodEndDate = request.PeriodEndDate,
        Status = PayrollRunStatus.Draft
    };

    dbContext.AddEntity(period);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/payroll/periods/{period.Id}", period);
})
    .AddEndpointFilter<ValidationFilter<CreatePayrollPeriodRequest>>();

api.MapGet("/payroll/adjustments", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] (
    int year,
    int month,
    IApplicationDbContext dbContext) =>
{
    var rows = (from adjustment in dbContext.PayrollAdjustments
                join employee in dbContext.Employees on adjustment.EmployeeId equals employee.Id
                where adjustment.Year == year && adjustment.Month == month
                orderby employee.FirstName, employee.LastName
                select new
                {
                    adjustment.Id,
                    adjustment.EmployeeId,
                    EmployeeName = employee.FirstName + " " + employee.LastName,
                    adjustment.Year,
                    adjustment.Month,
                    adjustment.Type,
                    adjustment.Amount,
                    adjustment.Notes
                }).ToList();

    return Results.Ok(rows);
});

api.MapPost("/payroll/adjustments", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    CreatePayrollAdjustmentRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (request.Month is < 1 or > 12)
    {
        return Results.BadRequest(new { error = "Month must be between 1 and 12." });
    }

    var employeeExists = await dbContext.Employees.AnyAsync(x => x.Id == request.EmployeeId, cancellationToken);
    if (!employeeExists)
    {
        return Results.BadRequest(new { error = "Employee not found in this tenant." });
    }

    var adjustment = new PayrollAdjustment
    {
        EmployeeId = request.EmployeeId,
        Year = request.Year,
        Month = request.Month,
        Type = request.Type,
        Amount = request.Amount,
        Notes = request.Notes.Trim()
    };

    dbContext.AddEntity(adjustment);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/payroll/adjustments/{adjustment.Id}", adjustment);
})
    .AddEndpointFilter<ValidationFilter<CreatePayrollAdjustmentRequest>>();

api.MapPost("/payroll/runs/calculate", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    CalculatePayrollRunRequest request,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var period = await dbContext.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == request.PayrollPeriodId, cancellationToken);
    if (period is null)
    {
        return Results.NotFound(new { error = "Payroll period not found." });
    }

    var lockedRunExists = await dbContext.PayrollRuns.AnyAsync(x => x.PayrollPeriodId == period.Id && x.Status == PayrollRunStatus.Locked, cancellationToken);
    if (lockedRunExists)
    {
        return Results.BadRequest(new { error = "Payroll is already locked for this period." });
    }

    var run = await dbContext.PayrollRuns
        .Where(x => x.PayrollPeriodId == period.Id)
        .OrderByDescending(x => x.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    if (run is null)
    {
        run = new PayrollRun
        {
            PayrollPeriodId = period.Id,
            Status = PayrollRunStatus.Draft
        };
        dbContext.AddEntity(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    var existingLines = await dbContext.PayrollLines.Where(x => x.PayrollRunId == run.Id).ToListAsync(cancellationToken);
    if (existingLines.Count > 0)
    {
        dbContext.RemoveEntities(existingLines);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    var employees = await dbContext.Employees.OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ToListAsync(cancellationToken);
    var attendance = await dbContext.AttendanceInputs.Where(x => x.Year == period.Year && x.Month == period.Month).ToListAsync(cancellationToken);
    var adjustments = await dbContext.PayrollAdjustments.Where(x => x.Year == period.Year && x.Month == period.Month).ToListAsync(cancellationToken);
    var unpaidLeaves = await dbContext.LeaveRequests
        .Where(x =>
            x.LeaveType == LeaveType.Unpaid &&
            x.Status == LeaveRequestStatus.Approved &&
            x.StartDate <= period.PeriodEndDate &&
            x.EndDate >= period.PeriodStartDate)
        .ToListAsync(cancellationToken);

    foreach (var employee in employees)
    {
        var employeeAttendance = attendance.FirstOrDefault(x => x.EmployeeId == employee.Id);
        var overtimeHours = employeeAttendance?.OvertimeHours ?? 0m;

        var allowance = adjustments
            .Where(x => x.EmployeeId == employee.Id && x.Type == PayrollAdjustmentType.Allowance)
            .Sum(x => x.Amount);

        var manualDeduction = adjustments
            .Where(x => x.EmployeeId == employee.Id && x.Type == PayrollAdjustmentType.Deduction)
            .Sum(x => x.Amount);

        var unpaidLeaveDays = unpaidLeaves
            .Where(x => x.EmployeeId == employee.Id)
            .Sum(x =>
            {
                var overlapStart = x.StartDate > period.PeriodStartDate ? x.StartDate : period.PeriodStartDate;
                var overlapEnd = x.EndDate < period.PeriodEndDate ? x.EndDate : period.PeriodEndDate;
                return overlapEnd < overlapStart ? 0 : overlapEnd.DayNumber - overlapStart.DayNumber + 1;
            });

        var dailyRate = employee.BaseSalary / 30m;
        var unpaidLeaveDeduction = Math.Round(unpaidLeaveDays * dailyRate, 2);
        var gosiWageBase = employee.IsGosiEligible
            ? Math.Round(employee.GosiBasicWage + employee.GosiHousingAllowance, 2)
            : 0m;
        var gosiEmployeeContribution = employee.IsGosiEligible
            ? Math.Round(gosiWageBase * 0.09m, 2)
            : 0m;
        var gosiEmployerContribution = employee.IsGosiEligible
            ? Math.Round(gosiWageBase * 0.11m, 2)
            : 0m;
        var totalDeductions = Math.Round(manualDeduction + unpaidLeaveDeduction + gosiEmployeeContribution, 2);

        var overtimeRate = (employee.BaseSalary / 30m / 8m) * 1.5m;
        var overtimeAmount = Math.Round(overtimeHours * overtimeRate, 2);
        var net = Math.Round(employee.BaseSalary + allowance + overtimeAmount - totalDeductions, 2);

        dbContext.AddEntity(new PayrollLine
        {
            PayrollRunId = run.Id,
            EmployeeId = employee.Id,
            BaseSalary = employee.BaseSalary,
            Allowances = allowance,
            ManualDeductions = manualDeduction,
            UnpaidLeaveDays = unpaidLeaveDays,
            UnpaidLeaveDeduction = unpaidLeaveDeduction,
            GosiWageBase = gosiWageBase,
            GosiEmployeeContribution = gosiEmployeeContribution,
            GosiEmployerContribution = gosiEmployerContribution,
            Deductions = totalDeductions,
            OvertimeHours = overtimeHours,
            OvertimeAmount = overtimeAmount,
            NetAmount = net
        });
    }

    run.Status = PayrollRunStatus.Calculated;
    run.CalculatedAtUtc = dateTimeProvider.UtcNow;
    period.Status = PayrollRunStatus.Calculated;

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new { runId = run.Id, status = run.Status });
})
    .AddEndpointFilter<ValidationFilter<CalculatePayrollRunRequest>>();

api.MapGet("/payroll/runs/{runId:guid}", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid runId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    if (run is null)
    {
        return Results.NotFound();
    }

    var lines = (from line in dbContext.PayrollLines
                 join employee in dbContext.Employees on line.EmployeeId equals employee.Id
                 where line.PayrollRunId == runId
                 orderby employee.FirstName, employee.LastName
                 select new
                 {
                     line.Id,
                     line.EmployeeId,
                     EmployeeName = employee.FirstName + " " + employee.LastName,
                     line.BaseSalary,
                     line.Allowances,
                     line.ManualDeductions,
                     line.UnpaidLeaveDays,
                     line.UnpaidLeaveDeduction,
                     line.GosiWageBase,
                     line.GosiEmployeeContribution,
                     line.GosiEmployerContribution,
                     line.Deductions,
                     line.OvertimeHours,
                     line.OvertimeAmount,
                     line.NetAmount
                 }).ToList();

    return Results.Ok(new
    {
        run.Id,
        run.PayrollPeriodId,
        run.Status,
        run.CalculatedAtUtc,
        run.ApprovedAtUtc,
        run.LockedAtUtc,
        Lines = lines
    });
});

api.MapPost("/payroll/runs/{runId:guid}/approve", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    Guid runId,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    if (run is null)
    {
        return Results.NotFound();
    }

    if (run.Status != PayrollRunStatus.Calculated)
    {
        return Results.BadRequest(new { error = "Only calculated runs can be approved." });
    }

    run.Status = PayrollRunStatus.Approved;
    run.ApprovedAtUtc = dateTimeProvider.UtcNow;

    var period = await dbContext.PayrollPeriods.FirstAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
    period.Status = PayrollRunStatus.Approved;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { run.Id, run.Status });
});

api.MapPost("/payroll/runs/{runId:guid}/lock", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    Guid runId,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    if (run is null)
    {
        return Results.NotFound();
    }

    if (run.Status != PayrollRunStatus.Approved)
    {
        return Results.BadRequest(new { error = "Only approved runs can be locked." });
    }

    run.Status = PayrollRunStatus.Locked;
    run.LockedAtUtc = dateTimeProvider.UtcNow;

    var period = await dbContext.PayrollPeriods.FirstAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
    period.Status = PayrollRunStatus.Locked;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { run.Id, run.Status });
});

api.MapPost("/payroll/runs/{runId:guid}/exports/register-csv", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid runId,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    if (run is null)
    {
        return Results.NotFound();
    }

    var period = await dbContext.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
    if (period is null)
    {
        return Results.NotFound();
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var exportJob = new ExportArtifact
    {
        PayrollRunId = runId,
        ArtifactType = "PayrollRegisterCsv",
        Status = ExportArtifactStatus.Pending,
        FileName = $"payroll-register-{period.Year}-{period.Month:00}-run-{runId.ToString()[..8]}.csv",
        ContentType = "text/csv",
        CreatedByUserId = userId
    };

    dbContext.AddEntity(exportJob);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Accepted($"/api/payroll/exports/{exportJob.Id}", new
    {
        exportJob.Id,
        exportJob.Status
    });
});

api.MapPost("/payroll/runs/{runId:guid}/exports/gosi-csv", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid runId,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    if (run is null)
    {
        return Results.NotFound();
    }

    var period = await dbContext.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
    if (period is null)
    {
        return Results.NotFound();
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var exportJob = new ExportArtifact
    {
        PayrollRunId = runId,
        ArtifactType = "GosiCsv",
        Status = ExportArtifactStatus.Pending,
        FileName = $"gosi-{period.Year}-{period.Month:00}-run-{runId.ToString()[..8]}.csv",
        ContentType = "text/csv",
        CreatedByUserId = userId
    };

    dbContext.AddEntity(exportJob);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Accepted($"/api/payroll/exports/{exportJob.Id}", new
    {
        exportJob.Id,
        exportJob.Status
    });
});

api.MapPost("/payroll/runs/{runId:guid}/exports/wps-csv", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid runId,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    if (run is null)
    {
        return Results.NotFound();
    }

    var period = await dbContext.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
    if (period is null)
    {
        return Results.NotFound();
    }

    var company = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (company is null)
    {
        return Results.NotFound(new { error = "Company profile not found." });
    }

    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(company.WpsCompanyBankName))
    {
        missing.Add("Company WPS bank name is missing.");
    }

    if (string.IsNullOrWhiteSpace(company.WpsCompanyBankCode))
    {
        missing.Add("Company WPS bank code is missing.");
    }

    if (string.IsNullOrWhiteSpace(company.WpsCompanyIban))
    {
        missing.Add("Company WPS IBAN is missing.");
    }

    var lineEmployees = await (from line in dbContext.PayrollLines
                               join employee in dbContext.Employees on line.EmployeeId equals employee.Id
                               where line.PayrollRunId == runId
                               select new
                               {
                                   EmployeeName = employee.FirstName + " " + employee.LastName,
                                   employee.EmployeeNumber,
                                   employee.BankName,
                                   employee.BankIban
                               }).ToListAsync(cancellationToken);

    if (lineEmployees.Count == 0)
    {
        missing.Add("No payroll lines found for selected run.");
    }

    foreach (var row in lineEmployees)
    {
        if (string.IsNullOrWhiteSpace(row.EmployeeNumber))
        {
            missing.Add($"Employee number missing for: {row.EmployeeName}");
        }

        if (string.IsNullOrWhiteSpace(row.BankName))
        {
            missing.Add($"Bank name missing for: {row.EmployeeName}");
        }

        if (string.IsNullOrWhiteSpace(row.BankIban))
        {
            missing.Add($"IBAN missing for: {row.EmployeeName}");
        }
    }

    if (missing.Count > 0)
    {
        return Results.BadRequest(new
        {
            error = "WPS export validation failed.",
            details = missing.Distinct().Take(30).ToArray()
        });
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var exportJob = new ExportArtifact
    {
        PayrollRunId = runId,
        ArtifactType = "WpsCsv",
        Status = ExportArtifactStatus.Pending,
        FileName = $"wps-{period.Year}-{period.Month:00}-run-{runId.ToString()[..8]}.csv",
        ContentType = "text/csv",
        CreatedByUserId = userId
    };

    dbContext.AddEntity(exportJob);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Accepted($"/api/payroll/exports/{exportJob.Id}", new
    {
        exportJob.Id,
        exportJob.Status
    });
});

api.MapPost("/payroll/runs/{runId:guid}/exports/payslip/{employeeId:guid}/pdf", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid runId,
    Guid employeeId,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    if (run is null)
    {
        return Results.NotFound();
    }

    var period = await dbContext.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
    if (period is null)
    {
        return Results.NotFound();
    }

    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    var lineExists = await dbContext.PayrollLines.AnyAsync(x => x.PayrollRunId == runId && x.EmployeeId == employeeId, cancellationToken);
    if (!lineExists)
    {
        return Results.BadRequest(new { error = "Payroll line not found for this employee in selected run." });
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var exportJob = new ExportArtifact
    {
        PayrollRunId = runId,
        EmployeeId = employeeId,
        ArtifactType = "PayslipPdf",
        Status = ExportArtifactStatus.Pending,
        FileName = $"payslip-{employee.FirstName}-{employee.LastName}-{period.Year}-{period.Month:00}.pdf".Replace(" ", "-"),
        ContentType = "application/pdf",
        CreatedByUserId = userId
    };

    dbContext.AddEntity(exportJob);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Accepted($"/api/payroll/exports/{exportJob.Id}", new
    {
        exportJob.Id,
        exportJob.Status
    });
});

api.MapGet("/payroll/runs/{runId:guid}/exports", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] (
    Guid runId,
    IApplicationDbContext dbContext) =>
{
    var artifacts = dbContext.ExportArtifacts
        .Where(x => x.PayrollRunId == runId)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.ArtifactType,
            x.Status,
            x.ErrorMessage,
            x.FileName,
            x.ContentType,
            x.SizeBytes,
            x.EmployeeId,
            x.CreatedAtUtc,
            x.CompletedAtUtc,
            x.CreatedByUserId
        })
        .ToList();

    return Results.Ok(artifacts);
});

api.MapGet("/payroll/exports/{exportId:guid}", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid exportId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var export = await dbContext.ExportArtifacts.FirstOrDefaultAsync(x => x.Id == exportId, cancellationToken);
    if (export is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        export.Id,
        export.PayrollRunId,
        export.EmployeeId,
        export.ArtifactType,
        export.Status,
        export.ErrorMessage,
        export.FileName,
        export.ContentType,
        export.SizeBytes,
        export.CreatedAtUtc,
        export.CompletedAtUtc
    });
});

api.MapGet("/payroll/exports/{exportId:guid}/download", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid exportId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var export = await dbContext.ExportArtifacts.FirstOrDefaultAsync(x => x.Id == exportId, cancellationToken);
    if (export is null)
    {
        return Results.NotFound();
    }

    if (export.Status != ExportArtifactStatus.Completed || export.FileData is null || export.FileData.Length == 0)
    {
        return Results.Conflict(new { error = "Export file is not ready yet." });
    }

    return Results.File(export.FileData, export.ContentType, export.FileName);
});

api.MapGet("/audit-logs", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] (
    int? page,
    int? pageSize,
    string? pathContains,
    IApplicationDbContext dbContext) =>
{
    var safePage = Math.Max(1, page ?? 1);
    var safePageSize = Math.Clamp(pageSize ?? 50, 1, 500);
    pathContains = string.IsNullOrWhiteSpace(pathContains) ? null : pathContains.Trim().ToLowerInvariant();

    var query = dbContext.AuditLogs.AsQueryable();
    if (pathContains is not null)
    {
        query = query.Where(x => x.Path.ToLower().Contains(pathContains));
    }

    var total = query.Count();

    var logs = query
        .OrderByDescending(x => x.CreatedAtUtc)
        .Skip((safePage - 1) * safePageSize)
        .Take(safePageSize)
        .Select(x => new
        {
            x.Id,
            x.CreatedAtUtc,
            x.TenantId,
            x.UserId,
            x.Method,
            x.Path,
            x.StatusCode,
            x.DurationMs,
            x.IpAddress
        })
        .ToList();

    return Results.Ok(new { items = logs, total, page = safePage, pageSize = safePageSize });
});

static async Task<ComplianceScoreResponse> BuildComplianceScoreAsync(IApplicationDbContext dbContext, CancellationToken cancellationToken)
{
    var company = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    var employeeItems = await dbContext.Employees
        .Select(x => new
        {
            x.IsSaudiNational,
            x.EmployeeNumber,
            x.BankName,
            x.BankIban
        })
        .ToListAsync(cancellationToken);

    var totalEmployees = employeeItems.Count;
    var saudiEmployees = employeeItems.Count(x => x.IsSaudiNational);
    var saudizationPercent = totalEmployees == 0 ? 0m : Math.Round(saudiEmployees * 100m / totalEmployees, 1);

    var wpsCompanyReady = company is not null &&
                          !string.IsNullOrWhiteSpace(company.WpsCompanyBankName) &&
                          !string.IsNullOrWhiteSpace(company.WpsCompanyBankCode) &&
                          !string.IsNullOrWhiteSpace(company.WpsCompanyIban);

    var employeesMissingPaymentData = employeeItems.Count(x =>
        string.IsNullOrWhiteSpace(x.EmployeeNumber) ||
        string.IsNullOrWhiteSpace(x.BankName) ||
        string.IsNullOrWhiteSpace(x.BankIban));

    var openAlerts = await dbContext.ComplianceAlerts
        .Where(x => !x.IsResolved)
        .Select(x => new { x.Severity })
        .ToListAsync(cancellationToken);

    var criticalAlerts = openAlerts.Count(x => x.Severity == "Critical");
    var warningAlerts = openAlerts.Count(x => x.Severity == "Warning");
    var noticeAlerts = openAlerts.Count(x => x.Severity == "Notice");

    var score = 100m;

    if (!wpsCompanyReady)
    {
        score -= 20m;
    }

    score -= Math.Min(20m, employeesMissingPaymentData * 2m);
    score -= Math.Min(20m, criticalAlerts * 4m);
    score -= Math.Min(10m, warningAlerts * 2m);
    score -= Math.Min(5m, noticeAlerts);

    if (saudizationPercent < 30m)
    {
        score -= Math.Min(20m, (30m - saudizationPercent) * 1.2m);
    }

    var finalScore = (int)Math.Clamp(Math.Round(score), 0m, 100m);
    var grade = finalScore >= 90 ? "A" :
        finalScore >= 80 ? "B" :
        finalScore >= 70 ? "C" : "D";

    var recommendations = new List<string>();
    if (!wpsCompanyReady)
    {
        recommendations.Add("Complete company WPS bank profile.");
    }

    if (employeesMissingPaymentData > 0)
    {
        recommendations.Add($"Complete payment profiles for {employeesMissingPaymentData} employees.");
    }

    if (criticalAlerts > 0)
    {
        recommendations.Add($"Resolve {criticalAlerts} critical compliance alerts within 7 days.");
    }

    if (warningAlerts > 0)
    {
        recommendations.Add($"Plan remediation for {warningAlerts} warning alerts within 30 days.");
    }

    if (saudizationPercent < 30m)
    {
        recommendations.Add("Raise Saudization ratio to at least 30% target.");
    }

    if (recommendations.Count == 0)
    {
        recommendations.Add("Maintain current controls and run weekly compliance review.");
    }

    return new ComplianceScoreResponse(
        finalScore,
        grade,
        saudizationPercent,
        wpsCompanyReady,
        employeesMissingPaymentData,
        criticalAlerts,
        warningAlerts,
        noticeAlerts,
        recommendations);
}

static string BuildComplianceDigestBodyText(
    string companyName,
    DateTime nowUtc,
    ComplianceScoreResponse score,
    IReadOnlyCollection<ComplianceDigestAlertItemResponse> topAlerts)
{
    var sb = new StringBuilder();
    sb.AppendLine($"Compliance Digest for {companyName}");
    sb.AppendLine($"Generated at (UTC): {nowUtc:yyyy-MM-dd HH:mm}");
    sb.AppendLine();
    sb.AppendLine($"Score: {score.Score}/100 ({score.Grade})");
    sb.AppendLine($"Saudization: {score.SaudizationPercent:F1}%");
    sb.AppendLine($"WPS Company Ready: {score.WpsCompanyReady}");
    sb.AppendLine($"Employees Missing Payment Data: {score.EmployeesMissingPaymentData}");
    sb.AppendLine($"Open Alerts: Critical={score.CriticalAlerts}, Warning={score.WarningAlerts}, Notice={score.NoticeAlerts}");
    sb.AppendLine();
    sb.AppendLine("Top Alerts:");

    if (topAlerts.Count == 0)
    {
        sb.AppendLine("- No open alerts.");
    }
    else
    {
        foreach (var alert in topAlerts)
        {
            sb.AppendLine($"- {alert.EmployeeName}: {alert.DocumentType} | {alert.Severity} | DaysLeft={alert.DaysLeft} | Expiry={alert.ExpiryDate:yyyy-MM-dd}");
        }
    }

    sb.AppendLine();
    sb.AppendLine("Action Priority: close critical in 7 days, warning in 30 days, and keep WPS/payment data complete.");
    return sb.ToString();
}

static string BuildComplianceDigestBodyHtml(
    string companyName,
    DateTime nowUtc,
    ComplianceScoreResponse score,
    IReadOnlyCollection<ComplianceDigestAlertItemResponse> topAlerts)
{
    var encodedCompany = WebUtility.HtmlEncode(companyName);
    var sb = new StringBuilder();
    sb.Append("<html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#14223b;\">");
    sb.Append($"<h2 style=\"margin-bottom:4px;\">Compliance Digest / ملخص الامتثال - {encodedCompany}</h2>");
    sb.Append($"<p style=\"margin-top:0;color:#4e6684;\">Generated (UTC): {nowUtc:yyyy-MM-dd HH:mm}</p>");
    sb.Append("<table style=\"border-collapse:collapse;width:100%;max-width:760px;\">");
    sb.Append("<tr><th style=\"text-align:left;border:1px solid #dbe6f2;padding:8px;background:#f4f8fd;\">Metric</th><th style=\"text-align:left;border:1px solid #dbe6f2;padding:8px;background:#f4f8fd;\">Value</th></tr>");
    sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">Score / الدرجة</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">{score.Score}/100 ({WebUtility.HtmlEncode(score.Grade)})</td></tr>");
    sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">Saudization / السعودة</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">{score.SaudizationPercent:F1}%</td></tr>");
    sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">WPS Ready / جاهزية WPS</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">{score.WpsCompanyReady}</td></tr>");
    sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">Missing Payment Data / نقص بيانات الدفع</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">{score.EmployeesMissingPaymentData}</td></tr>");
    sb.Append($"<tr><td style=\"border:1px solid #dbe6f2;padding:8px;\">Open Alerts / التنبيهات المفتوحة</td><td style=\"border:1px solid #dbe6f2;padding:8px;\">Critical={score.CriticalAlerts}, Warning={score.WarningAlerts}, Notice={score.NoticeAlerts}</td></tr>");
    sb.Append("</table>");

    sb.Append("<h3 style=\"margin-top:18px;\">Top Alerts / أهم التنبيهات</h3>");
    if (topAlerts.Count == 0)
    {
        sb.Append("<p>No open alerts / لا توجد تنبيهات مفتوحة.</p>");
    }
    else
    {
        sb.Append("<ul>");
        foreach (var alert in topAlerts)
        {
            sb.Append("<li>");
            sb.Append(WebUtility.HtmlEncode($"{alert.EmployeeName}: {alert.DocumentType} | {alert.Severity} | DaysLeft={alert.DaysLeft} | Expiry={alert.ExpiryDate:yyyy-MM-dd}"));
            sb.Append("</li>");
        }
        sb.Append("</ul>");
    }

    sb.Append("<p style=\"margin-top:16px;color:#344b66;\">Action Priority: close critical in 7 days, warning in 30 days, and keep WPS/payment data complete.</p>");
    sb.Append("<p style=\"color:#344b66;\">أولوية العمل: إغلاق التنبيهات الحرجة خلال 7 أيام، والمتوسطة خلال 30 يوما، واستكمال بيانات WPS والدفع.</p>");
    sb.Append("</body></html>");
    return sb.ToString();
}

static async Task<EmailSendResult> TrySendComplianceDigestEmailAsync(
    IConfiguration configuration,
    string toEmail,
    string subject,
    string textBody,
    string htmlBody,
    CancellationToken cancellationToken)
{
    var host = configuration["Smtp:Host"];
    var port = int.TryParse(configuration["Smtp:Port"], out var parsedPort) ? parsedPort : 587;
    var username = configuration["Smtp:Username"];
    var password = configuration["Smtp:Password"];
    var fromEmail = configuration["Smtp:FromEmail"];
    var fromName = configuration["Smtp:FromName"];
    var enableSsl = !string.Equals(configuration["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromEmail))
    {
        // Keep manual-send usable in dev before SMTP setup.
        return new EmailSendResult(true, true, null);
    }

    using var mail = new MailMessage
    {
        From = new MailAddress(fromEmail, string.IsNullOrWhiteSpace(fromName) ? "HR Payroll Compliance" : fromName),
        Subject = subject,
        Body = htmlBody,
        IsBodyHtml = true
    };
    mail.To.Add(toEmail);

    using var client = new SmtpClient(host, port)
    {
        EnableSsl = enableSsl
    };

    if (!string.IsNullOrWhiteSpace(username))
    {
        client.Credentials = new NetworkCredential(username, password ?? string.Empty);
    }

    try
    {
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(mail, cancellationToken);
        return new EmailSendResult(true, false, null);
    }
    catch (Exception ex)
    {
        return new EmailSendResult(false, false, ex.Message);
    }
}

app.Run();

public sealed record CreateTenantRequest(
    string TenantName,
    string Slug,
    string CompanyLegalName,
    string CurrencyCode,
    int DefaultPayDay,
    string OwnerFirstName,
    string OwnerLastName,
    string OwnerEmail,
    string OwnerPassword);

public sealed record LoginRequest(Guid? TenantId, string? TenantSlug, string Email, string Password);

public sealed record UpdateCompanyProfileRequest(
    string LegalName,
    string CurrencyCode,
    int DefaultPayDay,
    decimal EosFirstFiveYearsMonthFactor,
    decimal EosAfterFiveYearsMonthFactor,
    string WpsCompanyBankName,
    string WpsCompanyBankCode,
    string WpsCompanyIban,
    bool ComplianceDigestEnabled,
    string ComplianceDigestEmail,
    string ComplianceDigestFrequency,
    int ComplianceDigestHourUtc);

public sealed record CreateUserRequest(string FirstName, string LastName, string Email, string Password, string Role);

public sealed record CreateEmployeeRequest(
    DateOnly StartDate,
    string FirstName,
    string LastName,
    string Email,
    string JobTitle,
    decimal BaseSalary,
    bool IsSaudiNational,
    bool IsGosiEligible,
    decimal GosiBasicWage,
    decimal GosiHousingAllowance,
    string EmployeeNumber,
    string BankName,
    string BankIban,
    string IqamaNumber,
    DateOnly? IqamaExpiryDate,
    DateOnly? WorkPermitExpiryDate);

public sealed record EstimateEosRequest(DateOnly? TerminationDate);
public sealed record FinalSettlementEstimateRequest(
    DateOnly TerminationDate,
    int? Year,
    int? Month,
    decimal AdditionalManualDeduction,
    string? Notes);

public sealed record ResolveComplianceAlertRequest(string? Reason);
public sealed record ResolveComplianceAlertsBulkRequest(IReadOnlyCollection<Guid> AlertIds, string? Reason);
public sealed record ComplianceAiBriefRequest(string? Prompt, string? Language);
public sealed record ComplianceScoreHistoryItemResponse(
    DateOnly SnapshotDate,
    int Score,
    string Grade,
    decimal SaudizationPercent,
    bool WpsCompanyReady,
    int EmployeesMissingPaymentData,
    int CriticalAlerts,
    int WarningAlerts,
    int NoticeAlerts);
public sealed record ComplianceDigestAlertItemResponse(
    string EmployeeName,
    string DocumentType,
    int DaysLeft,
    string Severity,
    DateOnly ExpiryDate);
public sealed record EmailSendResult(bool Sent, bool Simulated, string? ErrorMessage = null);
public sealed record ComplianceScoreResponse(
    int Score,
    string Grade,
    decimal SaudizationPercent,
    bool WpsCompanyReady,
    int EmployeesMissingPaymentData,
    int CriticalAlerts,
    int WarningAlerts,
    int NoticeAlerts,
    IReadOnlyCollection<string> Recommendations);

public sealed record UpsertAttendanceInputRequest(
    Guid EmployeeId,
    int Year,
    int Month,
    int DaysPresent,
    int DaysAbsent,
    decimal OvertimeHours);

public sealed record CreateLeaveRequestRequest(
    Guid? EmployeeId,
    LeaveType LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason);

public sealed record RejectLeaveRequestRequest(string RejectionReason);

public sealed record CreatePayrollPeriodRequest(
    int Year,
    int Month,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate);

public sealed record CreatePayrollAdjustmentRequest(
    Guid EmployeeId,
    int Year,
    int Month,
    PayrollAdjustmentType Type,
    decimal Amount,
    string Notes);

public sealed record CalculatePayrollRunRequest(Guid PayrollPeriodId);
