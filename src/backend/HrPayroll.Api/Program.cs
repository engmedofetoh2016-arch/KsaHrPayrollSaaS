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
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<CreateTenantRequestValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthLogin", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 8,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddCors(options =>
{
    var configuredOrigins = builder.Configuration["Cors:AllowedOrigins"];
    var productionOrigins = (configuredOrigins ?? string.Empty)
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var defaultOrigins = new[]
    {
        "http://localhost:4200",
        "http://127.0.0.1:4200",
        "http://localhost:4201",
        "http://127.0.0.1:4201",
        "http://localhost:4202",
        "http://127.0.0.1:4202"
    };

    options.AddPolicy("WebDev", policy =>
    {
        if (productionOrigins.Length > 0)
        {
            policy.WithOrigins(productionOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
            return;
        }

        // Fallback for local dev and sslip.io preview hosts when no env CORS list is provided.
        policy.SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            (
                defaultOrigins.Contains($"{uri.Scheme}://{uri.Host}:{uri.Port}", StringComparer.OrdinalIgnoreCase) ||
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".sslip.io", StringComparison.OrdinalIgnoreCase)
            ))
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
app.UseRateLimiter();
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
        EosAfterFiveYearsMonthFactor = 1.0m,
        NitaqatActivity = "General",
        NitaqatSizeBand = "Small",
        NitaqatTargetPercent = 30m
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
        EmailConfirmed = true,
        LockoutEnabled = true
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
    SignInManager<ApplicationUser> signInManager,
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

    if (!user.LockoutEnabled)
    {
        user.LockoutEnabled = true;
        await userManager.UpdateAsync(user);
    }

    if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow.AddYears(50))
    {
        return Results.Json(new { error = "Account is disabled. Contact your administrator." }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (await userManager.IsLockedOutAsync(user))
    {
        return Results.Json(new { error = "Account locked due to repeated failed logins. Try again in 15 minutes." }, statusCode: StatusCodes.Status423Locked);
    }

    var signInResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
    if (signInResult.IsLockedOut)
    {
        return Results.Json(new { error = "Account locked due to repeated failed logins. Try again in 15 minutes." }, statusCode: StatusCodes.Status423Locked);
    }

    if (!signInResult.Succeeded)
    {
        return Results.Unauthorized();
    }

    var roles = await userManager.GetRolesAsync(user);
    var claims = await userManager.GetClaimsAsync(user);
    var mustChangePassword = claims.Any(x =>
        string.Equals(x.Type, "must_change_password", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.Value, "true", StringComparison.OrdinalIgnoreCase));
    var token = tokenGenerator.GenerateToken(user.Id, user.Email ?? string.Empty, user.TenantId, roles.ToArray());

    return Results.Ok(new
    {
        accessToken = token,
        mustChangePassword,
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
    .RequireRateLimiting("AuthLogin")
    .AllowAnonymous();

api.MapPost("/auth/forgot-password", async (
    ForgotPasswordRequest request,
    IConfiguration configuration,
    IApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    var genericResult = Results.Ok(new { message = "If the account exists, reset instructions were sent." });

    var tenantSlug = request.TenantSlug.Trim().ToLowerInvariant();
    var tenant = await dbContext.Tenants
        .FirstOrDefaultAsync(x => x.Slug == tenantSlug && x.IsActive, cancellationToken);
    if (tenant is null)
    {
        return genericResult;
    }

    var normalizedEmail = request.Email.Trim().ToUpperInvariant();
    var user = await userManager.Users
        .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.NormalizedEmail == normalizedEmail, cancellationToken);
    if (user is null)
    {
        return genericResult;
    }

    var token = await userManager.GeneratePasswordResetTokenAsync(user);
    var appBaseUrl = configuration["App:BaseUrl"] ?? configuration["APP_URL"] ?? "http://localhost:4200";
    var resetLink =
        $"{appBaseUrl.TrimEnd('/')}/reset-password?tenantSlug={Uri.EscapeDataString(tenant.Slug)}&email={Uri.EscapeDataString(user.Email ?? request.Email)}&token={Uri.EscapeDataString(token)}";

    var subject = "Reset your password";
    var textBody = $"Use this link to reset your password: {resetLink}";
    var htmlBody =
        $"<p>You requested a password reset.</p><p><a href=\"{WebUtility.HtmlEncode(resetLink)}\">Reset Password</a></p><p>If you did not request this, you can ignore this email.</p>";

    await TrySendComplianceDigestEmailAsync(configuration, user.Email ?? request.Email, subject, textBody, htmlBody, cancellationToken);
    return genericResult;
})
    .AddEndpointFilter<ValidationFilter<ForgotPasswordRequest>>()
    .AllowAnonymous();

api.MapPost("/auth/reset-password", async (
    ResetPasswordRequest request,
    IApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    var tenantSlug = request.TenantSlug.Trim().ToLowerInvariant();
    var tenant = await dbContext.Tenants
        .FirstOrDefaultAsync(x => x.Slug == tenantSlug && x.IsActive, cancellationToken);
    if (tenant is null)
    {
        return Results.BadRequest(new { error = "Invalid reset request." });
    }

    var normalizedEmail = request.Email.Trim().ToUpperInvariant();
    var user = await userManager.Users
        .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.NormalizedEmail == normalizedEmail, cancellationToken);
    if (user is null)
    {
        return Results.BadRequest(new { error = "Invalid reset request." });
    }

    var resetResult = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
    if (!resetResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Failed to reset password.", details = resetResult.Errors.Select(x => x.Description) });
    }

    var claims = await userManager.GetClaimsAsync(user);
    var mustChangeClaims = claims.Where(x => x.Type == "must_change_password").ToArray();
    foreach (var claim in mustChangeClaims)
    {
        await userManager.RemoveClaimAsync(user, claim);
    }

    return Results.Ok(new { message = "Password reset successful." });
})
    .AddEndpointFilter<ValidationFilter<ResetPasswordRequest>>()
    .AllowAnonymous();

api.MapPost("/auth/change-password", [Authorize] async (
    ChangePasswordRequest request,
    HttpContext httpContext,
    UserManager<ApplicationUser> userManager) =>
{
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdClaim, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await userManager.FindByIdAsync(userId.ToString());
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var changeResult = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
    if (!changeResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Failed to change password.", details = changeResult.Errors.Select(x => x.Description) });
    }

    var claims = await userManager.GetClaimsAsync(user);
    var mustChangeClaims = claims.Where(x => x.Type == "must_change_password").ToArray();
    foreach (var claim in mustChangeClaims)
    {
        await userManager.RemoveClaimAsync(user, claim);
    }

    return Results.Ok(new { message = "Password changed successfully." });
})
    .AddEndpointFilter<ValidationFilter<ChangePasswordRequest>>();

api.MapGet("/me/profile", [Authorize] async (
    ITenantContext tenantContext,
    HttpContext httpContext,
    UserManager<ApplicationUser> userManager,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(userIdClaim, out var userId))
    {
        return Results.Unauthorized();
    }

    var user = await userManager.Users
        .Where(x => x.Id == userId && x.TenantId == tenantContext.TenantId)
        .Select(x => new
        {
            x.Id,
            x.TenantId,
            x.FirstName,
            x.LastName,
            x.Email
        })
        .FirstOrDefaultAsync(cancellationToken);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    var normalizedEmail = (user.Email ?? string.Empty).Trim().ToUpperInvariant();
    var employee = await dbContext.Employees
        .Where(x => x.Email.ToUpper() == normalizedEmail)
        .Select(x => new
        {
            x.Id,
            x.StartDate,
            x.FirstName,
            x.LastName,
            x.Email,
            x.JobTitle,
            x.BaseSalary,
            x.EmployeeNumber,
            x.BankName,
            x.BankIban,
            x.IqamaNumber,
            x.IqamaExpiryDate,
            x.WorkPermitExpiryDate,
            x.ContractEndDate
        })
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(new
    {
        user,
        employee
    });
});

api.MapGet("/me/eos-estimate", [Authorize(Roles = RoleNames.Employee)] async (
    DateOnly? terminationDate,
    ITenantContext tenantContext,
    HttpContext httpContext,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var userEmail = httpContext.User.Claims
        .Where(x => x.Type is "email" or ClaimTypes.Email)
        .Select(x => x.Value)
        .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(userEmail))
    {
        return Results.BadRequest(new { error = "Your account email was not found in token." });
    }

    var normalizedEmail = userEmail.Trim().ToUpperInvariant();
    var employee = await dbContext.Employees
        .Where(x => x.Email.ToUpper() == normalizedEmail)
        .Select(x => new
        {
            x.Id,
            x.StartDate,
            x.BaseSalary
        })
        .FirstOrDefaultAsync(cancellationToken);

    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee profile not found for current user." });
    }

    var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    if (profile is null)
    {
        return Results.NotFound(new { error = "Company profile not found." });
    }

    var safeTerminationDate = terminationDate ?? DateOnly.FromDateTime(dateTimeProvider.UtcNow.Date);
    if (safeTerminationDate < employee.StartDate)
    {
        return Results.BadRequest(new { error = "Termination date cannot be before employee start date." });
    }

    var serviceDays = safeTerminationDate.DayNumber - employee.StartDate.DayNumber + 1;
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
        terminationDate = safeTerminationDate,
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
});

api.MapGet("/me/salary-certificate/pdf", [Authorize(Roles = RoleNames.Employee)] async (
    string? purpose,
    ITenantContext tenantContext,
    HttpContext httpContext,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var userEmail = httpContext.User.Claims
        .Where(x => x.Type is "email" or ClaimTypes.Email)
        .Select(x => x.Value)
        .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(userEmail))
    {
        return Results.BadRequest(new { error = "Your account email was not found in token." });
    }

    var normalizedEmail = userEmail.Trim().ToUpperInvariant();
    var employee = await dbContext.Employees
        .Where(x => x.Email.ToUpper() == normalizedEmail)
        .Select(x => new
        {
            x.FirstName,
            x.LastName,
            x.JobTitle,
            x.BaseSalary,
            x.EmployeeNumber,
            x.StartDate
        })
        .FirstOrDefaultAsync(cancellationToken);

    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee profile not found for current user." });
    }

    var company = await dbContext.CompanyProfiles
        .Select(x => new { x.LegalName, x.CurrencyCode })
        .FirstOrDefaultAsync(cancellationToken);

    var companyName = string.IsNullOrWhiteSpace(company?.LegalName) ? "Company" : company!.LegalName.Trim();
    var currency = string.IsNullOrWhiteSpace(company?.CurrencyCode) ? "SAR" : company!.CurrencyCode.Trim().ToUpperInvariant();
    var issuedAt = DateTime.UtcNow;
    var cleanPurpose = string.IsNullOrWhiteSpace(purpose)
        ? string.Empty
        : (purpose.Trim().Length > 120 ? purpose.Trim()[..120] : purpose.Trim());

    var pdf = Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Margin(30);
            page.Size(PageSizes.A4);
            page.Content().Column(col =>
            {
                col.Spacing(7);
                col.Item().Text(companyName).FontSize(18).SemiBold();
                col.Item().Text("Salary Certificate").FontSize(13).SemiBold();
                col.Item().PaddingVertical(6).LineHorizontal(1);
                col.Item().Text($"Employee Name: {employee.FirstName} {employee.LastName}");
                col.Item().Text($"Employee Number: {employee.EmployeeNumber}");
                col.Item().Text($"Job Title: {employee.JobTitle}");
                col.Item().Text($"Start Date: {employee.StartDate:yyyy-MM-dd}");
                col.Item().Text($"Monthly Base Salary: {employee.BaseSalary:F2} {currency}");
                if (!string.IsNullOrWhiteSpace(cleanPurpose))
                {
                    col.Item().Text($"Purpose: {cleanPurpose}");
                }

                col.Item().PaddingVertical(6).LineHorizontal(1);
                col.Item().Text($"Issued At (UTC): {issuedAt:yyyy-MM-dd HH:mm}");
                col.Item().Text("This certificate is issued electronically without signature.");
            });
        });
    }).GeneratePdf();

    var fileName = $"salary-certificate-{employee.FirstName}-{employee.LastName}-{issuedAt:yyyyMMdd}.pdf".Replace(" ", "-");
    return Results.File(pdf, "application/pdf", fileName);
});

api.MapGet("/me/payslips", [Authorize(Roles = RoleNames.Employee)] async (
    ITenantContext tenantContext,
    HttpContext httpContext,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var userEmail = httpContext.User.Claims
        .Where(x => x.Type is "email" or ClaimTypes.Email)
        .Select(x => x.Value)
        .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(userEmail))
    {
        return Results.BadRequest(new { error = "Your account email was not found in token." });
    }

    var normalizedEmail = userEmail.Trim().ToUpperInvariant();
    var employee = await dbContext.Employees
        .Where(x => x.Email.ToUpper() == normalizedEmail)
        .Select(x => new { x.Id })
        .FirstOrDefaultAsync(cancellationToken);

    if (employee is null)
    {
        return Results.BadRequest(new { error = "No employee profile linked to your account email." });
    }

    var rows = await (from artifact in dbContext.ExportArtifacts
                      join run in dbContext.PayrollRuns on artifact.PayrollRunId equals run.Id
                      join period in dbContext.PayrollPeriods on run.PayrollPeriodId equals period.Id
                      where artifact.ArtifactType == "PayslipPdf" && artifact.EmployeeId == employee.Id
                      orderby artifact.CreatedAtUtc descending
                      select new
                      {
                          artifact.Id,
                          artifact.PayrollRunId,
                          artifact.EmployeeId,
                          artifact.ArtifactType,
                          artifact.Status,
                          artifact.ErrorMessage,
                          artifact.FileName,
                          artifact.ContentType,
                          artifact.SizeBytes,
                          artifact.CreatedAtUtc,
                          artifact.CompletedAtUtc,
                          PeriodYear = period.Year,
                          PeriodMonth = period.Month
                      }).ToListAsync(cancellationToken);

    return Results.Ok(rows);
});

api.MapGet("/me/payslips/latest", [Authorize(Roles = RoleNames.Employee)] async (
    ITenantContext tenantContext,
    HttpContext httpContext,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var userEmail = httpContext.User.Claims
        .Where(x => x.Type is "email" or ClaimTypes.Email)
        .Select(x => x.Value)
        .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(userEmail))
    {
        return Results.BadRequest(new { error = "Your account email was not found in token." });
    }

    var normalizedEmail = userEmail.Trim().ToUpperInvariant();
    var employee = await dbContext.Employees
        .Where(x => x.Email.ToUpper() == normalizedEmail)
        .Select(x => new { x.Id })
        .FirstOrDefaultAsync(cancellationToken);

    if (employee is null)
    {
        return Results.BadRequest(new { error = "No employee profile linked to your account email." });
    }

    var latest = await (from artifact in dbContext.ExportArtifacts
                        join run in dbContext.PayrollRuns on artifact.PayrollRunId equals run.Id
                        join period in dbContext.PayrollPeriods on run.PayrollPeriodId equals period.Id
                        where artifact.ArtifactType == "PayslipPdf" && artifact.EmployeeId == employee.Id
                        orderby artifact.CreatedAtUtc descending
                        select new
                        {
                            artifact.Id,
                            artifact.PayrollRunId,
                            artifact.EmployeeId,
                            artifact.ArtifactType,
                            artifact.Status,
                            artifact.ErrorMessage,
                            artifact.FileName,
                            artifact.ContentType,
                            artifact.SizeBytes,
                            artifact.CreatedAtUtc,
                            artifact.CompletedAtUtc,
                            PeriodYear = period.Year,
                            PeriodMonth = period.Month
                        }).FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(latest);
});

api.MapGet("/me/payslips/{exportId:guid}/download", [Authorize(Roles = RoleNames.Employee)] async (
    Guid exportId,
    ITenantContext tenantContext,
    HttpContext httpContext,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var userEmail = httpContext.User.Claims
        .Where(x => x.Type is "email" or ClaimTypes.Email)
        .Select(x => x.Value)
        .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(userEmail))
    {
        return Results.BadRequest(new { error = "Your account email was not found in token." });
    }

    var normalizedEmail = userEmail.Trim().ToUpperInvariant();
    var employee = await dbContext.Employees
        .Where(x => x.Email.ToUpper() == normalizedEmail)
        .Select(x => new { x.Id })
        .FirstOrDefaultAsync(cancellationToken);

    if (employee is null)
    {
        return Results.BadRequest(new { error = "No employee profile linked to your account email." });
    }

    var export = await dbContext.ExportArtifacts.FirstOrDefaultAsync(
        x => x.Id == exportId && x.ArtifactType == "PayslipPdf" && x.EmployeeId == employee.Id,
        cancellationToken);

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
    profile.NitaqatActivity = string.IsNullOrWhiteSpace(request.NitaqatActivity) ? "General" : request.NitaqatActivity.Trim();
    profile.NitaqatSizeBand = string.IsNullOrWhiteSpace(request.NitaqatSizeBand) ? "Small" : request.NitaqatSizeBand.Trim();
    profile.NitaqatTargetPercent = Math.Clamp(request.NitaqatTargetPercent, 0m, 100m);

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
        EmailConfirmed = true,
        LockoutEnabled = true
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
            x.TenantId,
            x.AccessFailedCount,
            x.LockoutEnd
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(new { items = users, total, page = safePage, pageSize = safePageSize });
});

api.MapPost("/users/{userId:guid}/unlock", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    Guid userId,
    ITenantContext tenantContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var user = await userManager.Users
        .FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantContext.TenantId, cancellationToken);

    if (user is null)
    {
        return Results.NotFound(new { error = "User not found." });
    }

    user.LockoutEnabled = true;
    user.LockoutEnd = null;
    user.AccessFailedCount = 0;

    var updateResult = await userManager.UpdateAsync(user);
    if (!updateResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Failed to unlock user.", details = updateResult.Errors.Select(x => x.Description) });
    }

    return Results.Ok(new
    {
        user.Id,
        user.Email,
        user.FirstName,
        user.LastName,
        user.TenantId,
        user.AccessFailedCount,
        user.LockoutEnd
    });
});

api.MapPost("/users/{userId:guid}/disable", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    Guid userId,
    ITenantContext tenantContext,
    HttpContext httpContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var callerIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(callerIdClaim, out var callerId))
    {
        return Results.Unauthorized();
    }

    if (callerId == userId)
    {
        return Results.BadRequest(new { error = "You cannot disable your own account." });
    }

    var user = await userManager.Users
        .FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantContext.TenantId, cancellationToken);

    if (user is null)
    {
        return Results.NotFound(new { error = "User not found." });
    }

    user.LockoutEnabled = true;
    user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
    user.AccessFailedCount = 0;

    var updateResult = await userManager.UpdateAsync(user);
    if (!updateResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Failed to disable user.", details = updateResult.Errors.Select(x => x.Description) });
    }

    return Results.Ok(new
    {
        user.Id,
        user.Email,
        user.FirstName,
        user.LastName,
        user.TenantId,
        user.AccessFailedCount,
        user.LockoutEnd
    });
});

api.MapPost("/users/{userId:guid}/enable", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    Guid userId,
    ITenantContext tenantContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var user = await userManager.Users
        .FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantContext.TenantId, cancellationToken);

    if (user is null)
    {
        return Results.NotFound(new { error = "User not found." });
    }

    user.LockoutEnabled = true;
    user.LockoutEnd = null;
    user.AccessFailedCount = 0;

    var updateResult = await userManager.UpdateAsync(user);
    if (!updateResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Failed to enable user.", details = updateResult.Errors.Select(x => x.Description) });
    }

    return Results.Ok(new
    {
        user.Id,
        user.Email,
        user.FirstName,
        user.LastName,
        user.TenantId,
        user.AccessFailedCount,
        user.LockoutEnd
    });
});

api.MapPost("/users/{userId:guid}/admin-reset-password", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    Guid userId,
    AdminResetUserPasswordRequest request,
    ITenantContext tenantContext,
    HttpContext httpContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var callerIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!Guid.TryParse(callerIdClaim, out var callerId))
    {
        return Results.Unauthorized();
    }

    var user = await userManager.Users
        .FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantContext.TenantId, cancellationToken);

    if (user is null)
    {
        return Results.NotFound(new { error = "User not found." });
    }

    IdentityResult passwordResult;
    if (await userManager.HasPasswordAsync(user))
    {
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        passwordResult = await userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);
    }
    else
    {
        passwordResult = await userManager.AddPasswordAsync(user, request.NewPassword);
    }

    if (!passwordResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Failed to reset user password.", details = passwordResult.Errors.Select(x => x.Description) });
    }

    // Force user to change this admin-set password at next login.
    var claims = await userManager.GetClaimsAsync(user);
    var existingFlagClaims = claims.Where(x => x.Type == "must_change_password").ToArray();
    foreach (var claim in existingFlagClaims)
    {
        await userManager.RemoveClaimAsync(user, claim);
    }
    await userManager.AddClaimAsync(user, new Claim("must_change_password", "true"));

    // Also unlock user in case previous failed attempts locked account.
    user.LockoutEnabled = true;
    user.LockoutEnd = null;
    user.AccessFailedCount = 0;
    var updateResult = await userManager.UpdateAsync(user);
    if (!updateResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Password changed but failed to update user state.", details = updateResult.Errors.Select(x => x.Description) });
    }

    return Results.Ok(new
    {
        user.Id,
        user.Email,
        user.FirstName,
        user.LastName,
        user.TenantId,
        ForcePasswordChange = true
    });
})
    .AddEndpointFilter<ValidationFilter<AdminResetUserPasswordRequest>>();

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
            x.ContractEndDate,
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
        WorkPermitExpiryDate = request.WorkPermitExpiryDate,
        ContractEndDate = request.ContractEndDate
    };

    dbContext.AddEntity(employee);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/employees/{employee.Id}", employee);
})
    .AddEndpointFilter<ValidationFilter<CreateEmployeeRequest>>();

api.MapPut("/employees/{employeeId:guid}", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid employeeId,
    UpdateEmployeeRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    employee.StartDate = request.StartDate;
    employee.FirstName = request.FirstName.Trim();
    employee.LastName = request.LastName.Trim();
    employee.Email = request.Email.Trim();
    employee.JobTitle = request.JobTitle.Trim();
    employee.BaseSalary = request.BaseSalary;
    employee.IsSaudiNational = request.IsSaudiNational;
    employee.IsGosiEligible = request.IsGosiEligible;
    employee.GosiBasicWage = request.IsGosiEligible ? request.GosiBasicWage : 0m;
    employee.GosiHousingAllowance = request.IsGosiEligible ? request.GosiHousingAllowance : 0m;
    employee.EmployeeNumber = (request.EmployeeNumber ?? string.Empty).Trim();
    employee.BankName = (request.BankName ?? string.Empty).Trim();
    employee.BankIban = (request.BankIban ?? string.Empty).Trim().ToUpperInvariant();
    employee.IqamaNumber = (request.IqamaNumber ?? string.Empty).Trim();
    employee.IqamaExpiryDate = request.IqamaExpiryDate;
    employee.WorkPermitExpiryDate = request.WorkPermitExpiryDate;
    employee.ContractEndDate = request.ContractEndDate;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(employee);
})
    .AddEndpointFilter<ValidationFilter<UpdateEmployeeRequest>>();

api.MapDelete("/employees/{employeeId:guid}", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid employeeId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    dbContext.RemoveEntities([employee]);
    try
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException)
    {
        return Results.BadRequest(new { error = "Cannot delete employee because related records exist." });
    }

    return Results.NoContent();
});

api.MapPost("/employees/{employeeId:guid}/create-user-login", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid employeeId,
    CreateEmployeeLoginRequest request,
    ITenantContext tenantContext,
    IApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    CancellationToken cancellationToken) =>
{
    if (tenantContext.TenantId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Tenant was not resolved." });
    }

    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    var email = employee.Email.Trim();
    var normalizedEmail = email.ToUpperInvariant();
    var exists = await userManager.Users.AnyAsync(x => x.TenantId == tenantContext.TenantId && x.NormalizedEmail == normalizedEmail, cancellationToken);
    if (exists)
    {
        return Results.BadRequest(new { error = "A user login already exists for this employee email." });
    }

    var user = new ApplicationUser
    {
        TenantId = tenantContext.TenantId,
        FirstName = employee.FirstName.Trim(),
        LastName = employee.LastName.Trim(),
        Email = email,
        UserName = email,
        NormalizedEmail = normalizedEmail,
        NormalizedUserName = normalizedEmail,
        EmailConfirmed = true,
        LockoutEnabled = true
    };

    var createResult = await userManager.CreateAsync(user, request.Password);
    if (!createResult.Succeeded)
    {
        return Results.BadRequest(new { error = "Failed to create user.", details = createResult.Errors.Select(x => x.Description) });
    }

    await userManager.AddToRoleAsync(user, RoleNames.Employee);
    await userManager.AddClaimAsync(user, new Claim("must_change_password", "true"));

    return Results.Created($"/api/users/{user.Id}", new
    {
        user.Id,
        user.Email,
        user.FirstName,
        user.LastName,
        Role = RoleNames.Employee
    });
})
    .AddEndpointFilter<ValidationFilter<CreateEmployeeLoginRequest>>();

api.MapGet("/employees/{employeeId:guid}/salary-certificate/pdf", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid employeeId,
    string? purpose,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees
        .Where(x => x.Id == employeeId)
        .Select(x => new
        {
            x.FirstName,
            x.LastName,
            x.JobTitle,
            x.BaseSalary,
            x.EmployeeNumber,
            x.StartDate
        })
        .FirstOrDefaultAsync(cancellationToken);

    if (employee is null)
    {
        return Results.NotFound(new { error = "Employee not found." });
    }

    var company = await dbContext.CompanyProfiles
        .Select(x => new { x.LegalName, x.CurrencyCode })
        .FirstOrDefaultAsync(cancellationToken);

    var companyName = string.IsNullOrWhiteSpace(company?.LegalName) ? "Company" : company!.LegalName.Trim();
    var currency = string.IsNullOrWhiteSpace(company?.CurrencyCode) ? "SAR" : company!.CurrencyCode.Trim().ToUpperInvariant();
    var issuedAt = DateTime.UtcNow;
    var cleanPurpose = string.IsNullOrWhiteSpace(purpose)
        ? string.Empty
        : (purpose.Trim().Length > 120 ? purpose.Trim()[..120] : purpose.Trim());

    var pdf = Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Margin(30);
            page.Size(PageSizes.A4);
            page.Content().Column(col =>
            {
                col.Spacing(7);
                col.Item().Text(companyName).FontSize(18).SemiBold();
                col.Item().Text("Salary Certificate").FontSize(13).SemiBold();
                col.Item().PaddingVertical(6).LineHorizontal(1);
                col.Item().Text($"Employee Name: {employee.FirstName} {employee.LastName}");
                col.Item().Text($"Employee Number: {employee.EmployeeNumber}");
                col.Item().Text($"Job Title: {employee.JobTitle}");
                col.Item().Text($"Start Date: {employee.StartDate:yyyy-MM-dd}");
                col.Item().Text($"Monthly Base Salary: {employee.BaseSalary:F2} {currency}");
                if (!string.IsNullOrWhiteSpace(cleanPurpose))
                {
                    col.Item().Text($"Purpose: {cleanPurpose}");
                }

                col.Item().PaddingVertical(6).LineHorizontal(1);
                col.Item().Text($"Issued At (UTC): {issuedAt:yyyy-MM-dd HH:mm}");
                col.Item().Text("This certificate is issued electronically without signature.");
            });
        });
    }).GeneratePdf();

    var fileName = $"salary-certificate-{employee.FirstName}-{employee.LastName}-{issuedAt:yyyyMMdd}.pdf".Replace(" ", "-");
    return Results.File(pdf, "application/pdf", fileName);
});

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

    var finalSettlementBlocker = await OffboardingWorkflowGuards.GetFinalSettlementBlockerAsync(employee.Id, dbContext, cancellationToken);
    if (!string.IsNullOrWhiteSpace(finalSettlementBlocker))
    {
        return Results.BadRequest(new { error = finalSettlementBlocker });
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
    var payableSalaryDays = Math.Max(0, periodEnd.DayNumber - periodStart.DayNumber + 1);
    var pendingSalaryAmount = Math.Round(payableSalaryDays * dailyRate, 2);
    var unpaidLeaveDeduction = Math.Round(unpaidLeaveDays * dailyRate, 2);

    var annualLeaveBalance = await dbContext.LeaveBalances
        .Where(x =>
            x.EmployeeId == employee.Id &&
            x.Year == targetYear &&
            x.LeaveType == LeaveType.Annual)
        .Select(x => new { x.AllocatedDays, x.UsedDays })
        .FirstOrDefaultAsync(cancellationToken);

    var leaveEncashmentDays = annualLeaveBalance is null
        ? 0m
        : Math.Max(0m, annualLeaveBalance.AllocatedDays - annualLeaveBalance.UsedDays);
    var leaveEncashmentAmount = Math.Round(leaveEncashmentDays * dailyRate, 2);

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
    var settlementGross = Math.Round(eosAmount + pendingSalaryAmount + leaveEncashmentAmount, 2);
    var netSettlement = Math.Round(settlementGross - totalDeductions, 2);

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
        payableSalaryDays,
        pendingSalaryAmount,
        leaveEncashmentDays,
        leaveEncashmentAmount,
        unpaidLeaveDays,
        unpaidLeaveDeduction,
        manualDeductionsFromPayroll = manualDeductions,
        additionalManualDeduction,
        settlementGross,
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

    var finalSettlementBlocker = await OffboardingWorkflowGuards.GetFinalSettlementBlockerAsync(employee.Id, dbContext, cancellationToken);
    if (!string.IsNullOrWhiteSpace(finalSettlementBlocker))
    {
        return Results.BadRequest(new { error = finalSettlementBlocker });
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
    var payableSalaryDays = Math.Max(0, periodEnd.DayNumber - periodStart.DayNumber + 1);
    var pendingSalaryAmount = Math.Round(payableSalaryDays * dailyRate, 2);
    var unpaidLeaveDeduction = Math.Round(unpaidLeaveDays * dailyRate, 2);

    var annualLeaveBalance = await dbContext.LeaveBalances
        .Where(x =>
            x.EmployeeId == employee.Id &&
            x.Year == targetYear &&
            x.LeaveType == LeaveType.Annual)
        .Select(x => new { x.AllocatedDays, x.UsedDays })
        .FirstOrDefaultAsync(cancellationToken);

    var leaveEncashmentDays = annualLeaveBalance is null
        ? 0m
        : Math.Max(0m, annualLeaveBalance.AllocatedDays - annualLeaveBalance.UsedDays);
    var leaveEncashmentAmount = Math.Round(leaveEncashmentDays * dailyRate, 2);

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
    var settlementGross = Math.Round(eosAmount + pendingSalaryAmount + leaveEncashmentAmount, 2);
    var netSettlement = Math.Round(settlementGross - totalDeductions, 2);

    var csv = new StringBuilder();
    csv.AppendLine("Final Settlement Statement");
    csv.AppendLine($"Employee,{employee.FirstName} {employee.LastName}");
    csv.AppendLine($"Start Date,{employee.StartDate:dd-MM-yyyy}");
    csv.AppendLine($"Termination Date,{request.TerminationDate:dd-MM-yyyy}");
    csv.AppendLine($"Period,{targetMonth:00}-{targetYear}");
    csv.AppendLine($"Currency,{profile.CurrencyCode}");
    csv.AppendLine();
    csv.AppendLine("Item,Amount");
    csv.AppendLine($"EOS Amount,{eosAmount:F2}");
    csv.AppendLine($"Pending Salary ({payableSalaryDays} days),{pendingSalaryAmount:F2}");
    csv.AppendLine($"Leave Encashment ({leaveEncashmentDays:F2} days),{leaveEncashmentAmount:F2}");
    csv.AppendLine($"Settlement Gross,{settlementGross:F2}");
    csv.AppendLine($"Unpaid Leave Deduction,{unpaidLeaveDeduction:F2}");
    csv.AppendLine($"Manual Deductions (Payroll),{manualDeductions:F2}");
    csv.AppendLine($"Additional Manual Deduction,{additionalManualDeduction:F2}");
    csv.AppendLine($"Total Deductions,{totalDeductions:F2}");
    csv.AppendLine($"Net Final Settlement,{netSettlement:F2}");
    csv.AppendLine();
    csv.AppendLine($"Service Days,{serviceDays}");
    csv.AppendLine($"Service Years,{serviceYears:F4}");
    csv.AppendLine($"EOS Months,{eosMonths:F4}");
    csv.AppendLine($"Payable Salary Days,{payableSalaryDays}");
    csv.AppendLine($"Leave Encashment Days,{leaveEncashmentDays:F2}");
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

    var finalSettlementBlocker = await OffboardingWorkflowGuards.GetFinalSettlementBlockerAsync(employee.Id, dbContext, cancellationToken);
    if (!string.IsNullOrWhiteSpace(finalSettlementBlocker))
    {
        return Results.BadRequest(new { error = finalSettlementBlocker });
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
    var payableSalaryDays = Math.Max(0, periodEnd.DayNumber - periodStart.DayNumber + 1);
    var pendingSalaryAmount = Math.Round(payableSalaryDays * dailyRate, 2);
    var unpaidLeaveDeduction = Math.Round(unpaidLeaveDays * dailyRate, 2);

    var annualLeaveBalance = await dbContext.LeaveBalances
        .Where(x =>
            x.EmployeeId == employee.Id &&
            x.Year == targetYear &&
            x.LeaveType == LeaveType.Annual)
        .Select(x => new { x.AllocatedDays, x.UsedDays })
        .FirstOrDefaultAsync(cancellationToken);

    var leaveEncashmentDays = annualLeaveBalance is null
        ? 0m
        : Math.Max(0m, annualLeaveBalance.AllocatedDays - annualLeaveBalance.UsedDays);
    var leaveEncashmentAmount = Math.Round(leaveEncashmentDays * dailyRate, 2);

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
    var settlementGross = Math.Round(eosAmount + pendingSalaryAmount + leaveEncashmentAmount, 2);
    var netSettlement = Math.Round(settlementGross - totalDeductions, 2);

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
                col.Item().Text("Final Settlement Letter / بيان التسوية النهائية").FontSize(13).SemiBold();
                col.Item().Text($"Employee / الموظف: {employee.FirstName} {employee.LastName}");
                col.Item().Text($"Start Date / تاريخ المباشرة: {employee.StartDate:dd-MM-yyyy}");
                col.Item().Text($"Termination Date / تاريخ نهاية الخدمة: {request.TerminationDate:dd-MM-yyyy}");
                col.Item().Text($"Period / الفترة: {targetMonth:00}-{targetYear}");
                col.Item().Text($"Currency / العملة: {profile.CurrencyCode}");
                col.Item().PaddingVertical(8).LineHorizontal(1);
                col.Item().Text($"EOS Amount / مكافأة نهاية الخدمة: {eosAmount:F2}");
                col.Item().Text($"Pending Salary ({payableSalaryDays} days) / الراتب المستحق: {pendingSalaryAmount:F2}");
                col.Item().Text($"Leave Encashment ({leaveEncashmentDays:F2} days) / بدل رصيد الإجازة: {leaveEncashmentAmount:F2}");
                col.Item().Text($"Settlement Gross / إجمالي المستحقات: {settlementGross:F2}");
                col.Item().Text($"Unpaid Leave Deduction / خصم إجازة غير مدفوعة: {unpaidLeaveDeduction:F2}");
                col.Item().Text($"Manual Deductions (Payroll) / خصومات الرواتب: {manualDeductions:F2}");
                col.Item().Text($"Additional Manual Deduction / خصم إضافي: {additionalManualDeduction:F2}");
                col.Item().Text($"Total Deductions / إجمالي الخصومات: {totalDeductions:F2}");
                col.Item().PaddingVertical(8).LineHorizontal(1);
                col.Item().Text($"Net Final Settlement / صافي التسوية النهائية: {netSettlement:F2} {profile.CurrencyCode}").FontSize(14).Bold();
                col.Item().PaddingTop(8).Text($"Service Years / سنوات الخدمة: {serviceYears:F4}");
                col.Item().Text($"EOS Months / أشهر المكافأة: {eosMonths:F4}");
                col.Item().Text($"Payable Salary Days / أيام الراتب المستحق: {payableSalaryDays}");
                col.Item().Text($"Leave Encashment Days / أيام بدل الإجازة: {leaveEncashmentDays:F2}");
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

    var finalSettlementBlocker = await OffboardingWorkflowGuards.GetFinalSettlementBlockerAsync(employee.Id, dbContext, cancellationToken);
    if (!string.IsNullOrWhiteSpace(finalSettlementBlocker))
    {
        return Results.BadRequest(new { error = finalSettlementBlocker });
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

api.MapGet("/offboarding", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid? employeeId,
    string? status,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var query = from offboarding in dbContext.EmployeeOffboardings
                join employee in dbContext.Employees on offboarding.EmployeeId equals employee.Id
                orderby offboarding.CreatedAtUtc descending
                select new
                {
                    offboarding.Id,
                    offboarding.EmployeeId,
                    EmployeeName = employee.FirstName + " " + employee.LastName,
                    offboarding.EffectiveDate,
                    offboarding.Reason,
                    offboarding.Status,
                    offboarding.ApprovedAtUtc,
                    offboarding.PaidAtUtc,
                    offboarding.ClosedAtUtc,
                    offboarding.CreatedAtUtc
                };

    if (employeeId.HasValue && employeeId.Value != Guid.Empty)
    {
        query = query.Where(x => x.EmployeeId == employeeId.Value);
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(x => x.Status == status);
    }

    var rows = await query.Take(200).ToListAsync(cancellationToken);
    return Results.Ok(rows);
});

api.MapPost("/offboarding", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    CreateEmployeeOffboardingRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == request.EmployeeId, cancellationToken);
    if (employee is null)
    {
        return Results.BadRequest(new { error = "Employee not found in this tenant." });
    }

    var existingOpen = await dbContext.EmployeeOffboardings.AnyAsync(
        x => x.EmployeeId == request.EmployeeId && x.Status != "Closed",
        cancellationToken);

    if (existingOpen)
    {
        return Results.BadRequest(new { error = "An active offboarding record already exists for this employee." });
    }

    var createdBy = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : (Guid?)null;

    var offboarding = new EmployeeOffboarding
    {
        EmployeeId = request.EmployeeId,
        EffectiveDate = request.EffectiveDate,
        Reason = request.Reason.Trim(),
        Status = "Draft",
        RequestedByUserId = createdBy
    };

    dbContext.AddEntity(offboarding);
    await dbContext.SaveChangesAsync(cancellationToken);

    var checklist = new OffboardingChecklist
    {
        OffboardingId = offboarding.Id,
        EmployeeId = offboarding.EmployeeId,
        Status = "Open",
        Notes = string.Empty
    };
    dbContext.AddEntity(checklist);

    var checklistRoleName = string.IsNullOrWhiteSpace(request.ChecklistRoleName)
        ? employee.JobTitle.Trim()
        : request.ChecklistRoleName.Trim();

    var templates = await dbContext.OffboardingChecklistTemplates
        .Where(x => x.IsActive && x.RoleName == checklistRoleName)
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.ItemCode)
        .ToListAsync(cancellationToken);

    if (templates.Count > 0)
    {
        foreach (var template in templates)
        {
            dbContext.AddEntity(new OffboardingChecklistItem
            {
                ChecklistId = checklist.Id,
                ItemCode = template.ItemCode,
                ItemLabel = template.ItemLabel,
                Status = "Pending",
                Notes = $"TemplateRole:{template.RoleName};Required:{template.IsRequired};Approval:{template.RequiresApproval};Esign:{template.RequiresEsign}",
                SortOrder = template.SortOrder
            });
        }
    }
    else
    {
        var defaults = new[]
        {
            new { Code = "ID_CARD", Label = "Collect company ID card", Sort = 10 },
            new { Code = "IT_ACCESS", Label = "Revoke system and email access", Sort = 20 },
            new { Code = "ASSETS", Label = "Return company laptop and assets", Sort = 30 },
            new { Code = "FINANCE", Label = "Clear outstanding advances/loans", Sort = 40 },
            new { Code = "FINAL_SETTLEMENT", Label = "Final settlement reviewed and approved", Sort = 50 }
        };

        foreach (var item in defaults)
        {
            dbContext.AddEntity(new OffboardingChecklistItem
            {
                ChecklistId = checklist.Id,
                ItemCode = item.Code,
                ItemLabel = item.Label,
                Status = "Pending",
                Notes = "Required:True",
                SortOrder = item.Sort
            });
        }
    }

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/offboarding/{offboarding.Id}", new
    {
        offboarding.Id,
        offboarding.EmployeeId,
        offboarding.EffectiveDate,
        offboarding.Status
    });
})
    .AddEndpointFilter<ValidationFilter<CreateEmployeeOffboardingRequest>>();

api.MapPost("/offboarding/{offboardingId:guid}/approve", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid offboardingId,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var offboarding = await dbContext.EmployeeOffboardings.FirstOrDefaultAsync(x => x.Id == offboardingId, cancellationToken);
    if (offboarding is null)
    {
        return Results.NotFound(new { error = "Offboarding record not found." });
    }

    offboarding.Status = "Approved";
    offboarding.ApprovedAtUtc = dateTimeProvider.UtcNow;
    offboarding.ApprovedByUserId = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : null;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { offboarding.Id, offboarding.Status, offboarding.ApprovedAtUtc });
});

api.MapGet("/offboarding/{offboardingId:guid}/checklist", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid offboardingId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var checklist = await dbContext.OffboardingChecklists.FirstOrDefaultAsync(x => x.OffboardingId == offboardingId, cancellationToken);
    if (checklist is null)
    {
        return Results.NotFound(new { error = "Checklist not found for offboarding record." });
    }

    var items = await dbContext.OffboardingChecklistItems
        .Where(x => x.ChecklistId == checklist.Id)
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.ItemCode,
            x.ItemLabel,
            x.Status,
            x.Notes,
            x.SortOrder,
            x.CompletedAtUtc,
            x.CompletedByUserId
        })
        .ToListAsync(cancellationToken);

    var itemIds = items.Select(x => x.Id).ToList();
    var approvals = await dbContext.OffboardingChecklistApprovals
        .Where(x => itemIds.Contains(x.ChecklistItemId))
        .GroupBy(x => x.ChecklistItemId)
        .Select(g => new
        {
            ChecklistItemId = g.Key,
            LastStatus = g.OrderByDescending(x => x.CreatedAtUtc).Select(x => x.Status).FirstOrDefault()
        })
        .ToListAsync(cancellationToken);

    var esignCounts = await dbContext.OffboardingEsignDocuments
        .Where(x => itemIds.Contains(x.ChecklistItemId))
        .GroupBy(x => x.ChecklistItemId)
        .Select(g => new { ChecklistItemId = g.Key, Count = g.Count() })
        .ToListAsync(cancellationToken);

    var approvalByItem = approvals.ToDictionary(x => x.ChecklistItemId, x => x.LastStatus ?? string.Empty);
    var esignByItem = esignCounts.ToDictionary(x => x.ChecklistItemId, x => x.Count);

    var completedCount = items.Count(x => x.Status == "Completed" || x.Status == "Approved" || x.Status == "Signed");
    return Results.Ok(new
    {
        checklist.Id,
        checklist.OffboardingId,
        checklist.EmployeeId,
        checklist.Status,
        checklist.CompletedAtUtc,
        checklist.Notes,
        totalItems = items.Count,
        completedItems = completedCount,
        completionPercent = items.Count == 0 ? 0 : Math.Round(completedCount * 100m / items.Count, 1),
        items = items.Select(x => new
        {
            x.Id,
            x.ItemCode,
            x.ItemLabel,
            x.Status,
            x.Notes,
            x.SortOrder,
            x.CompletedAtUtc,
            x.CompletedByUserId,
            IsRequired = !x.Notes.Contains("Required:False", StringComparison.OrdinalIgnoreCase),
            RequiresApproval = x.Notes.Contains("Approval:True", StringComparison.OrdinalIgnoreCase),
            RequiresEsign = x.Notes.Contains("Esign:True", StringComparison.OrdinalIgnoreCase),
            ApprovalStatus = approvalByItem.TryGetValue(x.Id, out var approvalStatus) ? approvalStatus : null,
            EsignCount = esignByItem.TryGetValue(x.Id, out var esignCount) ? esignCount : 0
        })
    });
});

api.MapPost("/offboarding/{offboardingId:guid}/checklist/items", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid offboardingId,
    CreateOffboardingChecklistItemRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var checklist = await dbContext.OffboardingChecklists.FirstOrDefaultAsync(x => x.OffboardingId == offboardingId, cancellationToken);
    if (checklist is null)
    {
        return Results.NotFound(new { error = "Checklist not found for offboarding record." });
    }

    var exists = await dbContext.OffboardingChecklistItems.AnyAsync(
        x => x.ChecklistId == checklist.Id && x.ItemCode == request.ItemCode.Trim(),
        cancellationToken);

    if (exists)
    {
        return Results.BadRequest(new { error = "Checklist item code already exists in this checklist." });
    }

    var item = new OffboardingChecklistItem
    {
        ChecklistId = checklist.Id,
        ItemCode = request.ItemCode.Trim(),
        ItemLabel = request.ItemLabel.Trim(),
        Status = "Pending",
        Notes = (request.Notes ?? string.Empty).Trim(),
        SortOrder = request.SortOrder
    };

    dbContext.AddEntity(item);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/offboarding/{offboardingId}/checklist/items/{item.Id}", new
    {
        item.Id,
        item.ItemCode,
        item.ItemLabel,
        item.Status,
        item.SortOrder
    });
})
    .AddEndpointFilter<ValidationFilter<CreateOffboardingChecklistItemRequest>>();

api.MapPost("/offboarding/{offboardingId:guid}/checklist/items/{itemId:guid}/complete", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid offboardingId,
    Guid itemId,
    CompleteOffboardingChecklistItemRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var checklist = await dbContext.OffboardingChecklists.FirstOrDefaultAsync(x => x.OffboardingId == offboardingId, cancellationToken);
    if (checklist is null)
    {
        return Results.NotFound(new { error = "Checklist not found for offboarding record." });
    }

    var item = await dbContext.OffboardingChecklistItems.FirstOrDefaultAsync(
        x => x.Id == itemId && x.ChecklistId == checklist.Id,
        cancellationToken);

    if (item is null)
    {
        return Results.NotFound(new { error = "Checklist item not found." });
    }

    var completedByUserId = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : (Guid?)null;
    var requiresApproval = item.Notes.Contains("Approval:True", StringComparison.OrdinalIgnoreCase);
    var requiresEsign = item.Notes.Contains("Esign:True", StringComparison.OrdinalIgnoreCase);

    item.Status = requiresApproval
        ? "PendingApproval"
        : (requiresEsign ? "PendingEsign" : "Completed");
    item.CompletedAtUtc = dateTimeProvider.UtcNow;
    item.CompletedByUserId = completedByUserId;
    item.Notes = string.IsNullOrWhiteSpace(request.Notes) ? item.Notes : request.Notes.Trim();

    var totalItems = await dbContext.OffboardingChecklistItems.CountAsync(x => x.ChecklistId == checklist.Id, cancellationToken);
    var completedItems = await dbContext.OffboardingChecklistItems.CountAsync(
        x => x.ChecklistId == checklist.Id &&
             (x.Id == itemId
                ? (item.Status == "Completed" || item.Status == "Approved" || item.Status == "Signed")
                : (x.Status == "Completed" || x.Status == "Approved" || x.Status == "Signed")),
        cancellationToken);

    checklist.Status = completedItems == totalItems ? "Completed" : "InProgress";
    checklist.CompletedAtUtc = completedItems == totalItems ? dateTimeProvider.UtcNow : null;
    checklist.CompletedByUserId = completedItems == totalItems ? completedByUserId : null;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new
    {
        ItemId = item.Id,
        ItemStatus = item.Status,
        ItemCompletedAtUtc = item.CompletedAtUtc,
        ChecklistStatus = checklist.Status,
        ChecklistCompletedAtUtc = checklist.CompletedAtUtc
    });
})
    .AddEndpointFilter<ValidationFilter<CompleteOffboardingChecklistItemRequest>>();

api.MapPost("/offboarding/{offboardingId:guid}/checklist/items/{itemId:guid}/reopen", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid offboardingId,
    Guid itemId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var checklist = await dbContext.OffboardingChecklists.FirstOrDefaultAsync(x => x.OffboardingId == offboardingId, cancellationToken);
    if (checklist is null)
    {
        return Results.NotFound(new { error = "Checklist not found for offboarding record." });
    }

    var item = await dbContext.OffboardingChecklistItems.FirstOrDefaultAsync(
        x => x.Id == itemId && x.ChecklistId == checklist.Id,
        cancellationToken);

    if (item is null)
    {
        return Results.NotFound(new { error = "Checklist item not found." });
    }

    item.Status = "Pending";
    item.CompletedAtUtc = null;
    item.CompletedByUserId = null;
    checklist.Status = "InProgress";
    checklist.CompletedAtUtc = null;
    checklist.CompletedByUserId = null;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { ItemId = item.Id, ItemStatus = item.Status, ChecklistStatus = checklist.Status });
});

api.MapGet("/offboarding/checklist-templates", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    string? roleName,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var normalizedRole = string.IsNullOrWhiteSpace(roleName) ? null : roleName.Trim();
    var query = dbContext.OffboardingChecklistTemplates.Where(x => x.IsActive);
    if (!string.IsNullOrWhiteSpace(normalizedRole))
    {
        query = query.Where(x => x.RoleName == normalizedRole);
    }

    var rows = await query
        .OrderBy(x => x.RoleName)
        .ThenBy(x => x.SortOrder)
        .Select(x => new
        {
            x.Id,
            x.RoleName,
            x.ItemCode,
            x.ItemLabel,
            x.SortOrder,
            x.IsRequired,
            x.RequiresApproval,
            x.RequiresEsign,
            x.IsActive
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(rows);
});

api.MapPost("/offboarding/checklist-templates", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    CreateOffboardingChecklistTemplateRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var roleName = request.RoleName.Trim();
    var itemCode = request.ItemCode.Trim().ToUpperInvariant();
    var existing = await dbContext.OffboardingChecklistTemplates.FirstOrDefaultAsync(
        x => x.RoleName == roleName && x.ItemCode == itemCode,
        cancellationToken);

    if (existing is null)
    {
        var template = new OffboardingChecklistTemplate
        {
            RoleName = roleName,
            ItemCode = itemCode,
            ItemLabel = request.ItemLabel.Trim(),
            SortOrder = request.SortOrder,
            IsRequired = request.IsRequired,
            RequiresApproval = request.RequiresApproval,
            RequiresEsign = request.RequiresEsign,
            IsActive = request.IsActive
        };
        dbContext.AddEntity(template);
    }
    else
    {
        existing.ItemLabel = request.ItemLabel.Trim();
        existing.SortOrder = request.SortOrder;
        existing.IsRequired = request.IsRequired;
        existing.RequiresApproval = request.RequiresApproval;
        existing.RequiresEsign = request.RequiresEsign;
        existing.IsActive = request.IsActive;
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { roleName, itemCode });
})
    .AddEndpointFilter<ValidationFilter<CreateOffboardingChecklistTemplateRequest>>();

api.MapPost("/offboarding/{offboardingId:guid}/checklist/apply-template", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid offboardingId,
    ApplyOffboardingChecklistTemplateRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var checklist = await dbContext.OffboardingChecklists.FirstOrDefaultAsync(x => x.OffboardingId == offboardingId, cancellationToken);
    if (checklist is null)
    {
        return Results.NotFound(new { error = "Checklist not found for offboarding record." });
    }

    var roleName = request.RoleName.Trim();
    var templates = await dbContext.OffboardingChecklistTemplates
        .Where(x => x.IsActive && x.RoleName == roleName)
        .OrderBy(x => x.SortOrder)
        .ThenBy(x => x.ItemCode)
        .ToListAsync(cancellationToken);

    if (templates.Count == 0)
    {
        return Results.BadRequest(new { error = "No active checklist templates found for selected role." });
    }

    if (request.ReplaceExisting)
    {
        var existingItems = await dbContext.OffboardingChecklistItems.Where(x => x.ChecklistId == checklist.Id).ToListAsync(cancellationToken);
        if (existingItems.Count > 0)
        {
            dbContext.RemoveEntities(existingItems);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    var existingCodes = await dbContext.OffboardingChecklistItems
        .Where(x => x.ChecklistId == checklist.Id)
        .Select(x => x.ItemCode)
        .ToListAsync(cancellationToken);
    var existingSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var template in templates)
    {
        if (existingSet.Contains(template.ItemCode))
        {
            continue;
        }

        dbContext.AddEntity(new OffboardingChecklistItem
        {
            ChecklistId = checklist.Id,
            ItemCode = template.ItemCode,
            ItemLabel = template.ItemLabel,
            Status = "Pending",
            Notes = $"TemplateRole:{template.RoleName};Required:{template.IsRequired};Approval:{template.RequiresApproval};Esign:{template.RequiresEsign}",
            SortOrder = template.SortOrder
        });
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { checklist.Id, roleName, appliedCount = templates.Count });
})
    .AddEndpointFilter<ValidationFilter<ApplyOffboardingChecklistTemplateRequest>>();

api.MapPost("/offboarding/{offboardingId:guid}/checklist/items/{itemId:guid}/request-approval", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid offboardingId,
    Guid itemId,
    OffboardingChecklistApprovalRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var checklist = await dbContext.OffboardingChecklists.FirstOrDefaultAsync(x => x.OffboardingId == offboardingId, cancellationToken);
    if (checklist is null)
    {
        return Results.NotFound(new { error = "Checklist not found for offboarding record." });
    }

    var item = await dbContext.OffboardingChecklistItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ChecklistId == checklist.Id, cancellationToken);
    if (item is null)
    {
        return Results.NotFound(new { error = "Checklist item not found." });
    }

    var requestedBy = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
        ? parsedUserId
        : (Guid?)null;

    var approval = new OffboardingChecklistApproval
    {
        ChecklistItemId = item.Id,
        Status = "Pending",
        RequestedByUserId = requestedBy,
        RequestedAtUtc = dateTimeProvider.UtcNow,
        Notes = (request.Notes ?? string.Empty).Trim()
    };
    dbContext.AddEntity(approval);
    item.Status = "PendingApproval";

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new
    {
        ItemId = item.Id,
        ItemStatus = item.Status,
        ApprovalId = approval.Id,
        ApprovalStatus = approval.Status
    });
})
    .AddEndpointFilter<ValidationFilter<OffboardingChecklistApprovalRequest>>();

api.MapPost("/offboarding/{offboardingId:guid}/checklist/items/{itemId:guid}/approve", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid offboardingId,
    Guid itemId,
    OffboardingChecklistApprovalRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var checklist = await dbContext.OffboardingChecklists.FirstOrDefaultAsync(x => x.OffboardingId == offboardingId, cancellationToken);
    if (checklist is null)
    {
        return Results.NotFound(new { error = "Checklist not found for offboarding record." });
    }

    var item = await dbContext.OffboardingChecklistItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ChecklistId == checklist.Id, cancellationToken);
    if (item is null)
    {
        return Results.NotFound(new { error = "Checklist item not found." });
    }

    var approval = await dbContext.OffboardingChecklistApprovals
        .Where(x => x.ChecklistItemId == item.Id && x.Status == "Pending")
        .OrderByDescending(x => x.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    var approvedBy = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
        ? parsedUserId
        : (Guid?)null;

    if (approval is not null)
    {
        approval.Status = "Approved";
        approval.ApprovedByUserId = approvedBy;
        approval.ApprovedAtUtc = dateTimeProvider.UtcNow;
        approval.Notes = string.IsNullOrWhiteSpace(request.Notes) ? approval.Notes : request.Notes.Trim();
    }

    item.Status = "Approved";
    item.CompletedAtUtc = dateTimeProvider.UtcNow;
    item.CompletedByUserId = approvedBy;
    if (!string.IsNullOrWhiteSpace(request.Notes))
    {
        item.Notes = string.IsNullOrWhiteSpace(item.Notes) ? request.Notes.Trim() : $"{item.Notes}\n{request.Notes.Trim()}";
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { item.Id, item.Status });
})
    .AddEndpointFilter<ValidationFilter<OffboardingChecklistApprovalRequest>>();

api.MapPost("/offboarding/{offboardingId:guid}/checklist/items/{itemId:guid}/esign", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid offboardingId,
    Guid itemId,
    OffboardingChecklistEsignRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var checklist = await dbContext.OffboardingChecklists.FirstOrDefaultAsync(x => x.OffboardingId == offboardingId, cancellationToken);
    if (checklist is null)
    {
        return Results.NotFound(new { error = "Checklist not found for offboarding record." });
    }

    var item = await dbContext.OffboardingChecklistItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ChecklistId == checklist.Id, cancellationToken);
    if (item is null)
    {
        return Results.NotFound(new { error = "Checklist item not found." });
    }

    var signedBy = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
        ? parsedUserId
        : (Guid?)null;

    var esign = new OffboardingEsignDocument
    {
        ChecklistItemId = item.Id,
        DocumentName = request.DocumentName.Trim(),
        DocumentUrl = request.DocumentUrl.Trim(),
        Status = "Signed",
        SignedByUserId = signedBy,
        SignedAtUtc = dateTimeProvider.UtcNow
    };
    dbContext.AddEntity(esign);

    item.Status = "Signed";
    item.CompletedAtUtc = esign.SignedAtUtc;
    item.CompletedByUserId = signedBy;
    item.Notes = string.IsNullOrWhiteSpace(item.Notes) ? $"Esign:{esign.DocumentName}" : $"{item.Notes}\nEsign:{esign.DocumentName}";

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new
    {
        ItemId = item.Id,
        ItemStatus = item.Status,
        EsignId = esign.Id,
        esign.DocumentName
    });
})
    .AddEndpointFilter<ValidationFilter<OffboardingChecklistEsignRequest>>();

api.MapGet("/payroll/shift-rules", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var rows = await dbContext.ShiftRules
        .OrderBy(x => x.Name)
        .Select(x => new
        {
            x.Id,
            x.Name,
            x.StandardDailyHours,
            x.OvertimeMultiplierWeekday,
            x.OvertimeMultiplierWeekend,
            x.OvertimeMultiplierHoliday,
            x.WeekendDaysCsv,
            x.IsActive
        })
        .ToListAsync(cancellationToken);
    return Results.Ok(rows);
});

api.MapPost("/payroll/shift-rules", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    CreateShiftRuleRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var existing = await dbContext.ShiftRules.FirstOrDefaultAsync(x => x.Name == request.Name.Trim(), cancellationToken);
    if (existing is null)
    {
        dbContext.AddEntity(new ShiftRule
        {
            Name = request.Name.Trim(),
            StandardDailyHours = request.StandardDailyHours,
            OvertimeMultiplierWeekday = request.OvertimeMultiplierWeekday,
            OvertimeMultiplierWeekend = request.OvertimeMultiplierWeekend,
            OvertimeMultiplierHoliday = request.OvertimeMultiplierHoliday,
            WeekendDaysCsv = request.WeekendDaysCsv.Trim(),
            IsActive = request.IsActive
        });
    }
    else
    {
        existing.StandardDailyHours = request.StandardDailyHours;
        existing.OvertimeMultiplierWeekday = request.OvertimeMultiplierWeekday;
        existing.OvertimeMultiplierWeekend = request.OvertimeMultiplierWeekend;
        existing.OvertimeMultiplierHoliday = request.OvertimeMultiplierHoliday;
        existing.WeekendDaysCsv = request.WeekendDaysCsv.Trim();
        existing.IsActive = request.IsActive;
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok();
})
    .AddEndpointFilter<ValidationFilter<CreateShiftRuleRequest>>();

api.MapGet("/timesheets", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    int year,
    int month,
    Guid? employeeId,
    string? status,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var query = from sheet in dbContext.TimesheetEntries
                join employee in dbContext.Employees on sheet.EmployeeId equals employee.Id
                where sheet.WorkDate.Year == year && sheet.WorkDate.Month == month
                orderby sheet.WorkDate, employee.FirstName, employee.LastName
                select new
                {
                    sheet.Id,
                    sheet.EmployeeId,
                    EmployeeName = employee.FirstName + " " + employee.LastName,
                    sheet.WorkDate,
                    sheet.HoursWorked,
                    sheet.ApprovedOvertimeHours,
                    sheet.IsWeekend,
                    sheet.IsHoliday,
                    sheet.Status,
                    sheet.ShiftRuleId,
                    sheet.ApprovedAtUtc,
                    sheet.Notes
                };

    if (employeeId.HasValue && employeeId.Value != Guid.Empty)
    {
        query = query.Where(x => x.EmployeeId == employeeId.Value);
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        query = query.Where(x => x.Status == status.Trim());
    }

    var rows = await query.ToListAsync(cancellationToken);
    return Results.Ok(rows);
});

api.MapPost("/timesheets", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    UpsertTimesheetEntryRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == request.EmployeeId, cancellationToken);
    if (employee is null)
    {
        return Results.BadRequest(new { error = "Employee not found in this tenant." });
    }

    ShiftRule? rule = null;
    if (request.ShiftRuleId.HasValue && request.ShiftRuleId.Value != Guid.Empty)
    {
        rule = await dbContext.ShiftRules.FirstOrDefaultAsync(x => x.Id == request.ShiftRuleId.Value && x.IsActive, cancellationToken);
        if (rule is null)
        {
            return Results.BadRequest(new { error = "Shift rule not found or inactive." });
        }
    }

    var existing = await dbContext.TimesheetEntries.FirstOrDefaultAsync(
        x => x.EmployeeId == request.EmployeeId && x.WorkDate == request.WorkDate,
        cancellationToken);

    var weekendSet = (rule?.WeekendDaysCsv ?? "5,6")
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(x => int.TryParse(x, out var day) ? day : -1)
        .Where(x => x >= 0 && x <= 6)
        .ToHashSet();
    var dayOfWeekNumber = (int)request.WorkDate.DayOfWeek;
    var isWeekend = weekendSet.Contains(dayOfWeekNumber);
    var standardDailyHours = rule?.StandardDailyHours ?? 8m;
    var approvedOvertimeHours = Math.Max(0m, Math.Round(request.HoursWorked - standardDailyHours, 2));

    if (existing is null)
    {
        dbContext.AddEntity(new TimesheetEntry
        {
            EmployeeId = request.EmployeeId,
            ShiftRuleId = request.ShiftRuleId,
            WorkDate = request.WorkDate,
            HoursWorked = request.HoursWorked,
            ApprovedOvertimeHours = approvedOvertimeHours,
            IsWeekend = isWeekend,
            IsHoliday = request.IsHoliday,
            Status = "Pending",
            Notes = (request.Notes ?? string.Empty).Trim()
        });
    }
    else
    {
        existing.ShiftRuleId = request.ShiftRuleId;
        existing.HoursWorked = request.HoursWorked;
        existing.ApprovedOvertimeHours = approvedOvertimeHours;
        existing.IsWeekend = isWeekend;
        existing.IsHoliday = request.IsHoliday;
        existing.Status = "Pending";
        existing.ApprovedAtUtc = null;
        existing.ApprovedByUserId = null;
        existing.Notes = (request.Notes ?? string.Empty).Trim();
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok();
})
    .AddEndpointFilter<ValidationFilter<UpsertTimesheetEntryRequest>>();

api.MapPost("/timesheets/{entryId:guid}/approve", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid entryId,
    ApproveTimesheetEntryRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var entry = await dbContext.TimesheetEntries.FirstOrDefaultAsync(x => x.Id == entryId, cancellationToken);
    if (entry is null)
    {
        return Results.NotFound(new { error = "Timesheet entry not found." });
    }

    entry.Status = request.Approved ? "Approved" : "Rejected";
    entry.ApprovedAtUtc = dateTimeProvider.UtcNow;
    entry.ApprovedByUserId = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
        ? parsedUserId
        : null;

    if (request.ApprovedOvertimeHours.HasValue)
    {
        entry.ApprovedOvertimeHours = Math.Max(0m, Math.Round(request.ApprovedOvertimeHours.Value, 2));
    }

    if (!string.IsNullOrWhiteSpace(request.Notes))
    {
        entry.Notes = string.IsNullOrWhiteSpace(entry.Notes) ? request.Notes.Trim() : $"{entry.Notes}\n{request.Notes.Trim()}";
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { entry.Id, entry.Status, entry.ApprovedOvertimeHours });
})
    .AddEndpointFilter<ValidationFilter<ApproveTimesheetEntryRequest>>();

api.MapGet("/payroll/allowance-policies", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    bool? activeOnly,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var query = dbContext.AllowancePolicies.AsQueryable();
    if (activeOnly.GetValueOrDefault())
    {
        query = query.Where(x => x.IsActive);
    }

    var rows = await query
        .OrderBy(x => x.PolicyName)
        .Select(x => new
        {
            x.Id,
            x.PolicyName,
            x.JobTitle,
            x.MonthlyAmount,
            x.EffectiveFrom,
            x.EffectiveTo,
            x.IsTaxable,
            x.IsActive
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(rows);
});

api.MapPost("/payroll/allowance-policies", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    UpsertAllowancePolicyRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var policyName = request.PolicyName.Trim();
    var existing = await dbContext.AllowancePolicies.FirstOrDefaultAsync(x => x.PolicyName == policyName, cancellationToken);

    if (existing is null)
    {
        dbContext.AddEntity(new AllowancePolicy
        {
            PolicyName = policyName,
            JobTitle = request.JobTitle.Trim(),
            MonthlyAmount = Math.Round(request.MonthlyAmount, 2),
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsTaxable = request.IsTaxable,
            IsActive = request.IsActive
        });
    }
    else
    {
        existing.JobTitle = request.JobTitle.Trim();
        existing.MonthlyAmount = Math.Round(request.MonthlyAmount, 2);
        existing.EffectiveFrom = request.EffectiveFrom;
        existing.EffectiveTo = request.EffectiveTo;
        existing.IsTaxable = request.IsTaxable;
        existing.IsActive = request.IsActive;
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { policyName });
})
    .AddEndpointFilter<ValidationFilter<UpsertAllowancePolicyRequest>>();

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
            x.EmployeeNumber,
            x.IqamaNumber,
            x.IqamaExpiryDate,
            x.WorkPermitExpiryDate,
            x.ContractEndDate
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

        if (employee.ContractEndDate.HasValue)
        {
            var daysLeft = employee.ContractEndDate.Value.DayNumber - today.DayNumber;
            if (daysLeft >= 0 && daysLeft <= maxDays)
            {
                alerts.Add((
                    employee.Id,
                    employee.EmployeeName,
                    employee.IsSaudiNational,
                    "Contract",
                    employee.EmployeeNumber,
                    employee.ContractEndDate.Value,
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

api.MapGet("/compliance/alerts/{alertId:guid}/explain-risk", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid alertId,
    string? language,
    IApplicationDbContext dbContext,
    IComplianceAiService complianceAiService,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var alert = await dbContext.ComplianceAlerts.FirstOrDefaultAsync(x => x.Id == alertId, cancellationToken);
    if (alert is null)
    {
        return Results.NotFound(new { error = "Compliance alert not found." });
    }

    var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();
    var isArabic = normalizedLanguage.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
    var score = await BuildComplianceScoreAsync(dbContext, cancellationToken);
    var aiPrompt = BuildComplianceAlertRiskPrompt(alert, isArabic);
    var aiInput = new ComplianceAiInput(
        Language: normalizedLanguage,
        Score: score.Score,
        Grade: score.Grade,
        SaudizationPercent: score.SaudizationPercent,
        WpsCompanyReady: score.WpsCompanyReady,
        EmployeesMissingPaymentData: score.EmployeesMissingPaymentData,
        CriticalAlerts: score.CriticalAlerts,
        WarningAlerts: score.WarningAlerts,
        NoticeAlerts: score.NoticeAlerts,
        Recommendations: score.Recommendations,
        UserPrompt: aiPrompt);

    var aiResult = await complianceAiService.GenerateBriefAsync(aiInput, cancellationToken);
    var targetWindowDays = alert.DaysLeft <= 7 ? 7 : alert.DaysLeft <= 30 ? 30 : 60;

    return Results.Ok(new
    {
        alertId = alert.Id,
        alert.EmployeeName,
        alert.DocumentType,
        alert.Severity,
        alert.DaysLeft,
        alert.ExpiryDate,
        provider = aiResult.Provider,
        usedFallback = aiResult.UsedFallback,
        explanation = aiResult.Text,
        nextAction = BuildComplianceAlertNextAction(alert, isArabic),
        targetWindowDays,
        generatedAtUtc = dateTimeProvider.UtcNow
    });
});

api.MapGet("/compliance/score", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var score = await BuildComplianceScoreAsync(dbContext, cancellationToken);
    return Results.Ok(score);
});

api.MapGet("/compliance/saudization-simulation", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    int? plannedSaudiHires,
    int? plannedNonSaudiHires,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var safeSaudiHires = Math.Clamp(plannedSaudiHires ?? 1, 0, 500);
    var safeNonSaudiHires = Math.Clamp(plannedNonSaudiHires ?? 0, 0, 500);
    var company = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);

    var current = await dbContext.Employees
        .Select(x => new { x.IsSaudiNational })
        .ToListAsync(cancellationToken);

    var currentTotal = current.Count;
    var currentSaudi = current.Count(x => x.IsSaudiNational);
    var targetPercent = company is null
        ? 30m
        : Math.Clamp(company.NitaqatTargetPercent, 0m, 100m);
    var currentPercent = currentTotal == 0 ? 0m : Math.Round(currentSaudi * 100m / currentTotal, 1);

    var projectedSaudi = currentSaudi + safeSaudiHires;
    var projectedTotal = currentTotal + safeSaudiHires + safeNonSaudiHires;
    var projectedPercent = projectedTotal == 0 ? 0m : Math.Round(projectedSaudi * 100m / projectedTotal, 1);

    var requiredSaudiNow = currentTotal == 0
        ? 1
        : (int)Math.Ceiling((targetPercent / 100m) * currentTotal);
    var additionalSaudiNeededNow = Math.Max(0, requiredSaudiNow - currentSaudi);

    var requiredSaudiProjected = projectedTotal == 0
        ? 1
        : (int)Math.Ceiling((targetPercent / 100m) * projectedTotal);
    var additionalSaudiNeededProjected = Math.Max(0, requiredSaudiProjected - projectedSaudi);

    var currentRisk = ComputeSaudizationRiskLevel(currentPercent, targetPercent);
    var projectedRisk = ComputeSaudizationRiskLevel(projectedPercent, targetPercent);
    var currentBand = ComputeSaudizationBand(currentPercent, targetPercent);
    var projectedBand = ComputeSaudizationBand(projectedPercent, targetPercent);

    return Results.Ok(new
    {
        targetPercent,
        plannedSaudiHires = safeSaudiHires,
        plannedNonSaudiHires = safeNonSaudiHires,
        current = new
        {
            saudiEmployees = currentSaudi,
            totalEmployees = currentTotal,
            saudizationPercent = currentPercent,
            additionalSaudiEmployeesNeeded = additionalSaudiNeededNow,
            risk = currentRisk,
            band = currentBand
        },
        projected = new
        {
            saudiEmployees = projectedSaudi,
            totalEmployees = projectedTotal,
            saudizationPercent = projectedPercent,
            additionalSaudiEmployeesNeeded = additionalSaudiNeededProjected,
            risk = projectedRisk,
            band = projectedBand
        },
        deltaPercent = Math.Round(projectedPercent - currentPercent, 1),
        improvesBand = !string.Equals(currentBand, projectedBand, StringComparison.OrdinalIgnoreCase),
        improvesRisk = !string.Equals(currentRisk, projectedRisk, StringComparison.OrdinalIgnoreCase)
    });
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

api.MapPost("/leave/preview", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager + "," + RoleNames.Employee)] async (
    LeaveBalancePreviewRequest request,
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

    if (request.EndDate < request.StartDate)
    {
        return Results.BadRequest(new { error = "End date must be on or after start date." });
    }

    var requestedDays = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
    if (requestedDays <= 0)
    {
        return Results.BadRequest(new { error = "Requested leave days must be greater than zero." });
    }

    if (request.LeaveType == LeaveType.Unpaid)
    {
        return Results.Ok(new
        {
            employeeId = resolvedEmployeeId,
            request.LeaveType,
            request.StartDate,
            request.EndDate,
            requestedDays,
            allocatedDays = 0m,
            usedDays = 0m,
            remainingBefore = 0m,
            remainingAfter = 0m,
            canSubmit = true,
            message = "Unpaid leave does not consume balance."
        });
    }

    var year = request.StartDate.Year;
    var balance = await dbContext.LeaveBalances.FirstOrDefaultAsync(
        x => x.EmployeeId == resolvedEmployeeId && x.Year == year && x.LeaveType == request.LeaveType,
        cancellationToken);

    var allocatedDays = balance?.AllocatedDays ?? (request.LeaveType switch
    {
        LeaveType.Annual => 21m,
        LeaveType.Sick => 10m,
        _ => 0m
    });
    var usedDays = balance?.UsedDays ?? 0m;
    var remainingBefore = allocatedDays - usedDays;
    var remainingAfter = remainingBefore - requestedDays;
    var canSubmit = remainingAfter >= 0;

    return Results.Ok(new
    {
        employeeId = resolvedEmployeeId,
        request.LeaveType,
        request.StartDate,
        request.EndDate,
        requestedDays,
        allocatedDays,
        usedDays,
        remainingBefore,
        remainingAfter,
        canSubmit,
        message = canSubmit ? "Sufficient balance." : "Insufficient leave balance."
    });
})
    .AddEndpointFilter<ValidationFilter<LeaveBalancePreviewRequest>>();

api.MapGet("/leave/requests/{requestId:guid}/attachments", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager + "," + RoleNames.Employee)] async (
    Guid requestId,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var leaveRequest = await dbContext.LeaveRequests.FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
    if (leaveRequest is null)
    {
        return Results.NotFound(new { error = "Leave request not found." });
    }

    var canReview = httpContext.User.IsInRole(RoleNames.Owner) ||
                    httpContext.User.IsInRole(RoleNames.Admin) ||
                    httpContext.User.IsInRole(RoleNames.Hr) ||
                    httpContext.User.IsInRole(RoleNames.Manager);

    if (!canReview)
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

        if (ownEmployee is null || ownEmployee.Id != leaveRequest.EmployeeId)
        {
            return Results.Forbid();
        }
    }

    var metadata = BuildLeaveAttachmentMetadata(requestId);
    var items = await dbContext.ExportArtifacts
        .Where(x => x.ArtifactType == "LeaveAttachment" && x.MetadataJson == metadata)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.FileName,
            x.ContentType,
            x.SizeBytes,
            x.CreatedAtUtc,
            x.EmployeeId
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(items);
});

api.MapPost("/leave/requests/{requestId:guid}/attachments", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager + "," + RoleNames.Employee)] async (
    Guid requestId,
    HttpRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var leaveRequest = await dbContext.LeaveRequests.FirstOrDefaultAsync(x => x.Id == requestId, cancellationToken);
    if (leaveRequest is null)
    {
        return Results.NotFound(new { error = "Leave request not found." });
    }

    var canReview = httpContext.User.IsInRole(RoleNames.Owner) ||
                    httpContext.User.IsInRole(RoleNames.Admin) ||
                    httpContext.User.IsInRole(RoleNames.Hr) ||
                    httpContext.User.IsInRole(RoleNames.Manager);

    if (!canReview)
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

        if (ownEmployee is null || ownEmployee.Id != leaveRequest.EmployeeId)
        {
            return Results.Forbid();
        }
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Attachment upload requires multipart/form-data." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No attachment file provided." });
    }

    const long maxFileSize = 5 * 1024 * 1024;
    if (file.Length > maxFileSize)
    {
        return Results.BadRequest(new { error = "Attachment exceeds 5 MB size limit." });
    }

    var fileData = new byte[file.Length];
    await using (var stream = file.OpenReadStream())
    {
        await stream.ReadExactlyAsync(fileData, cancellationToken);
    }

    var artifact = new ExportArtifact
    {
        PayrollRunId = Guid.Empty,
        EmployeeId = leaveRequest.EmployeeId,
        ArtifactType = "LeaveAttachment",
        Status = ExportArtifactStatus.Completed,
        FileName = string.IsNullOrWhiteSpace(file.FileName) ? $"leave-attachment-{DateTime.UtcNow:yyyyMMddHHmmss}.bin" : file.FileName,
        ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
        FileData = fileData,
        SizeBytes = file.Length,
        CreatedByUserId = null,
        CompletedAtUtc = DateTime.UtcNow,
        ErrorMessage = string.Empty,
        MetadataJson = BuildLeaveAttachmentMetadata(requestId)
    };

    dbContext.AddEntity(artifact);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/leave/attachments/{artifact.Id}/download", new
    {
        artifact.Id,
        artifact.FileName,
        artifact.ContentType,
        artifact.SizeBytes,
        artifact.CreatedAtUtc
    });
});

api.MapGet("/leave/attachments/{attachmentId:guid}/download", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager + "," + RoleNames.Employee)] async (
    Guid attachmentId,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var attachment = await dbContext.ExportArtifacts.FirstOrDefaultAsync(
        x => x.Id == attachmentId && x.ArtifactType == "LeaveAttachment",
        cancellationToken);

    if (attachment is null || attachment.FileData is null || attachment.FileData.Length == 0)
    {
        return Results.NotFound(new { error = "Attachment not found." });
    }

    var canReview = httpContext.User.IsInRole(RoleNames.Owner) ||
                    httpContext.User.IsInRole(RoleNames.Admin) ||
                    httpContext.User.IsInRole(RoleNames.Hr) ||
                    httpContext.User.IsInRole(RoleNames.Manager);

    if (!canReview)
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

        if (ownEmployee is null || ownEmployee.Id != attachment.EmployeeId)
        {
            return Results.Forbid();
        }
    }

    return Results.File(attachment.FileData, attachment.ContentType, attachment.FileName);
});

api.MapPost("/leave/requests/{requestId:guid}/approve", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid requestId,
    ApproveLeaveRequestRequest request,
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
    leaveRequest.RejectionReason = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { leaveRequest.Id, leaveRequest.Status });
})
    .AddEndpointFilter<ValidationFilter<ApproveLeaveRequestRequest>>();

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

api.MapGet("/payroll/loans", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid? employeeId,
    string? status,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();

    var query = from loan in dbContext.EmployeeLoans
                join employee in dbContext.Employees on loan.EmployeeId equals employee.Id
                orderby loan.CreatedAtUtc descending
                select new
                {
                    loan.Id,
                    loan.EmployeeId,
                    EmployeeName = employee.FirstName + " " + employee.LastName,
                    loan.LoanType,
                    loan.PrincipalAmount,
                    loan.RemainingBalance,
                    loan.InstallmentAmount,
                    loan.StartYear,
                    loan.StartMonth,
                    loan.TotalInstallments,
                    loan.PaidInstallments,
                    loan.Status,
                    loan.Notes,
                    loan.CreatedAtUtc
                };

    if (employeeId.HasValue && employeeId.Value != Guid.Empty)
    {
        query = query.Where(x => x.EmployeeId == employeeId.Value);
    }

    if (!string.IsNullOrWhiteSpace(normalizedStatus))
    {
        query = query.Where(x => x.Status == normalizedStatus);
    }

    var rows = await query.Take(300).ToListAsync(cancellationToken);
    return Results.Ok(rows);
});

api.MapPost("/payroll/loans", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    CreateEmployeeLoanRequest request,
    IApplicationDbContext dbContext,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var employee = await dbContext.Employees.FirstOrDefaultAsync(x => x.Id == request.EmployeeId, cancellationToken);
    if (employee is null)
    {
        return Results.BadRequest(new { error = "Employee not found in this tenant." });
    }

    if (request.StartMonth is < 1 or > 12)
    {
        return Results.BadRequest(new { error = "Start month must be between 1 and 12." });
    }

    var loan = new EmployeeLoan
    {
        EmployeeId = request.EmployeeId,
        LoanType = string.IsNullOrWhiteSpace(request.LoanType) ? "Advance" : request.LoanType.Trim(),
        PrincipalAmount = Math.Round(request.PrincipalAmount, 2),
        RemainingBalance = Math.Round(request.PrincipalAmount, 2),
        InstallmentAmount = Math.Round(request.InstallmentAmount, 2),
        StartYear = request.StartYear,
        StartMonth = request.StartMonth,
        TotalInstallments = request.TotalInstallments,
        PaidInstallments = 0,
        Status = "Draft",
        Notes = (request.Notes ?? string.Empty).Trim(),
        CreatedByUserId = Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null
    };

    dbContext.AddEntity(loan);
    await dbContext.SaveChangesAsync(cancellationToken);

    var scheduleYear = request.StartYear;
    var scheduleMonth = request.StartMonth;
    var remaining = loan.PrincipalAmount;
    for (var i = 0; i < request.TotalInstallments; i++)
    {
        var amount = i == request.TotalInstallments - 1
            ? Math.Round(remaining, 2)
            : Math.Round(Math.Min(loan.InstallmentAmount, remaining), 2);

        remaining = Math.Max(0m, Math.Round(remaining - amount, 2));

        dbContext.AddEntity(new EmployeeLoanInstallment
        {
            EmployeeLoanId = loan.Id,
            EmployeeId = loan.EmployeeId,
            Year = scheduleYear,
            Month = scheduleMonth,
            Amount = amount,
            Status = "Pending"
        });

        scheduleMonth++;
        if (scheduleMonth > 12)
        {
            scheduleMonth = 1;
            scheduleYear++;
        }
    }

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/payroll/loans/{loan.Id}", new
    {
        loan.Id,
        loan.Status,
        loan.PrincipalAmount,
        loan.InstallmentAmount,
        loan.TotalInstallments
    });
})
    .AddEndpointFilter<ValidationFilter<CreateEmployeeLoanRequest>>();

api.MapPost("/payroll/loans/{loanId:guid}/approve", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid loanId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var loan = await dbContext.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == loanId, cancellationToken);
    if (loan is null)
    {
        return Results.NotFound(new { error = "Loan not found." });
    }

    if (loan.Status == "Closed" || loan.Status == "Cancelled")
    {
        return Results.BadRequest(new { error = $"Loan cannot be approved when status is {loan.Status}." });
    }

    loan.Status = "Active";
    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { loan.Id, loan.Status });
});

api.MapPost("/payroll/loans/{loanId:guid}/cancel", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid loanId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var loan = await dbContext.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == loanId, cancellationToken);
    if (loan is null)
    {
        return Results.NotFound(new { error = "Loan not found." });
    }

    loan.Status = "Cancelled";
    var pendingInstallments = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loan.Id && x.Status == "Pending")
        .ToListAsync(cancellationToken);

    foreach (var installment in pendingInstallments)
    {
        installment.Status = "Cancelled";
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { loan.Id, loan.Status });
});

api.MapGet("/payroll/loans/{loanId:guid}/lifecycle-check", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid loanId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var loan = await dbContext.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == loanId, cancellationToken);
    if (loan is null)
    {
        return Results.NotFound(new { error = "Loan not found." });
    }

    var pendingInstallments = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loan.Id && x.Status == "Pending")
        .Select(x => new { x.Id, x.Year, x.Month })
        .ToListAsync(cancellationToken);

    if (pendingInstallments.Count == 0)
    {
        return Results.Ok(new
        {
            CanReschedule = false,
            CanSkipNext = false,
            BlockedPeriods = Array.Empty<string>(),
            PendingInstallments = 0,
            NextPendingInstallmentId = (Guid?)null
        });
    }

    var years = pendingInstallments.Select(x => x.Year).Distinct().ToList();
    var months = pendingInstallments.Select(x => x.Month).Distinct().ToList();
    var pendingPeriodKeys = pendingInstallments
        .Select(x => $"{x.Year:D4}-{x.Month:D2}")
        .ToHashSet(StringComparer.Ordinal);

    var lockedPeriods = await (from period in dbContext.PayrollPeriods
                               join run in dbContext.PayrollRuns on period.Id equals run.PayrollPeriodId
                               where years.Contains(period.Year) &&
                                     months.Contains(period.Month) &&
                                     run.Status == PayrollRunStatus.Locked
                               select new { period.Year, period.Month })
        .Distinct()
        .ToListAsync(cancellationToken);

    var blockedPeriodKeys = lockedPeriods
        .Select(x => $"{x.Year:D4}-{x.Month:D2}")
        .Where(pendingPeriodKeys.Contains)
        .Distinct()
        .OrderBy(x => x)
        .ToList();

    var nextPendingInstallmentId = pendingInstallments
        .OrderBy(x => x.Year)
        .ThenBy(x => x.Month)
        .Select(x => x.Id)
        .FirstOrDefault();

    return Results.Ok(new
    {
        CanReschedule = blockedPeriodKeys.Count == 0,
        CanSkipNext = blockedPeriodKeys.Count == 0,
        BlockedPeriods = blockedPeriodKeys,
        PendingInstallments = pendingInstallments.Count,
        NextPendingInstallmentId = nextPendingInstallmentId
    });
});

api.MapPost("/payroll/loans/{loanId:guid}/reschedule", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid loanId,
    RescheduleEmployeeLoanRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var loan = await dbContext.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == loanId, cancellationToken);
    if (loan is null)
    {
        return Results.NotFound(new { error = "Loan not found." });
    }

    if (loan.Status is "Closed" or "Cancelled")
    {
        return Results.BadRequest(new { error = $"Loan cannot be rescheduled when status is {loan.Status}." });
    }

    var pendingInstallments = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loan.Id && x.Status == "Pending")
        .OrderBy(x => x.Year)
        .ThenBy(x => x.Month)
        .ToListAsync(cancellationToken);

    if (pendingInstallments.Count == 0)
    {
        return Results.BadRequest(new { error = "No pending installments available to reschedule." });
    }

    var years = pendingInstallments.Select(x => x.Year).Distinct().ToList();
    var months = pendingInstallments.Select(x => x.Month).Distinct().ToList();
    var pendingPeriodKeys = pendingInstallments
        .Select(x => $"{x.Year:D4}-{x.Month:D2}")
        .ToHashSet(StringComparer.Ordinal);

    var blockedPeriods = await (from period in dbContext.PayrollPeriods
                                join run in dbContext.PayrollRuns on period.Id equals run.PayrollPeriodId
                                where years.Contains(period.Year) &&
                                      months.Contains(period.Month) &&
                                      run.Status == PayrollRunStatus.Locked
                                select new { period.Year, period.Month })
        .Distinct()
        .ToListAsync(cancellationToken);

    var blockedKeys = blockedPeriods
        .Select(x => $"{x.Year:D4}-{x.Month:D2}")
        .Where(pendingPeriodKeys.Contains)
        .Distinct()
        .ToList();

    if (blockedKeys.Count > 0)
    {
        return Results.BadRequest(new { error = $"Cannot reschedule pending installments in locked payroll periods: {string.Join(", ", blockedKeys)}." });
    }

    var plannedPeriods = new List<(int Year, int Month)>(pendingInstallments.Count);
    var scheduleYear = request.StartYear;
    var scheduleMonth = request.StartMonth;
    for (var i = 0; i < pendingInstallments.Count; i++)
    {
        plannedPeriods.Add((scheduleYear, scheduleMonth));
        scheduleMonth++;
        if (scheduleMonth > 12)
        {
            scheduleMonth = 1;
            scheduleYear++;
        }
    }

    var nonPendingPeriodKeys = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loan.Id && x.Status != "Pending")
        .Select(x => $"{x.Year:D4}-{x.Month:D2}")
        .ToListAsync(cancellationToken);

    var nonPendingSet = nonPendingPeriodKeys.ToHashSet(StringComparer.Ordinal);
    var collidingPlannedKeys = plannedPeriods
        .Select(x => $"{x.Year:D4}-{x.Month:D2}")
        .Where(nonPendingSet.Contains)
        .Distinct()
        .ToList();

    if (collidingPlannedKeys.Count > 0)
    {
        return Results.BadRequest(new { error = $"Rescheduled timeline overlaps existing installments in periods: {string.Join(", ", collidingPlannedKeys)}." });
    }

    for (var i = 0; i < pendingInstallments.Count; i++)
    {
        pendingInstallments[i].Year = plannedPeriods[i].Year;
        pendingInstallments[i].Month = plannedPeriods[i].Month;
    }

    if (!string.IsNullOrWhiteSpace(request.Reason))
    {
        loan.Notes = string.IsNullOrWhiteSpace(loan.Notes)
            ? $"Reschedule: {request.Reason.Trim()}"
            : $"{loan.Notes}\nReschedule: {request.Reason.Trim()}";
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { loan.Id, loan.Status });
})
    .AddEndpointFilter<ValidationFilter<RescheduleEmployeeLoanRequest>>();

api.MapPost("/payroll/loans/{loanId:guid}/skip-next", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid loanId,
    SkipEmployeeLoanInstallmentRequest request,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var loan = await dbContext.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == loanId, cancellationToken);
    if (loan is null)
    {
        return Results.NotFound(new { error = "Loan not found." });
    }

    if (loan.Status is "Closed" or "Cancelled")
    {
        return Results.BadRequest(new { error = $"Loan cannot be modified when status is {loan.Status}." });
    }

    var nextPendingInstallment = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loan.Id && x.Status == "Pending")
        .OrderBy(x => x.Year)
        .ThenBy(x => x.Month)
        .FirstOrDefaultAsync(cancellationToken);

    if (nextPendingInstallment is null)
    {
        return Results.BadRequest(new { error = "No pending installments available to skip." });
    }

    var lockedPeriod = await (from period in dbContext.PayrollPeriods
                              join run in dbContext.PayrollRuns on period.Id equals run.PayrollPeriodId
                              where period.Year == nextPendingInstallment.Year &&
                                    period.Month == nextPendingInstallment.Month &&
                                    run.Status == PayrollRunStatus.Locked
                              select period.Id)
        .AnyAsync(cancellationToken);

    if (lockedPeriod)
    {
        return Results.BadRequest(new { error = $"Cannot skip installment for locked payroll period {nextPendingInstallment.Year:D4}-{nextPendingInstallment.Month:D2}." });
    }

    nextPendingInstallment.Status = "Skipped";

    var maxPeriod = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loan.Id)
        .OrderByDescending(x => x.Year)
        .ThenByDescending(x => x.Month)
        .Select(x => new { x.Year, x.Month })
        .FirstAsync(cancellationToken);

    var nextYear = maxPeriod.Year;
    var nextMonth = maxPeriod.Month + 1;
    if (nextMonth > 12)
    {
        nextMonth = 1;
        nextYear++;
    }

    dbContext.AddEntity(new EmployeeLoanInstallment
    {
        EmployeeLoanId = loan.Id,
        EmployeeId = loan.EmployeeId,
        Year = nextYear,
        Month = nextMonth,
        Amount = nextPendingInstallment.Amount,
        Status = "Pending"
    });

    loan.TotalInstallments += 1;
    if (!string.IsNullOrWhiteSpace(request.Reason))
    {
        loan.Notes = string.IsNullOrWhiteSpace(loan.Notes)
            ? $"Skip next: {request.Reason.Trim()}"
            : $"{loan.Notes}\nSkip next: {request.Reason.Trim()}";
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { loan.Id, loan.Status });
})
    .AddEndpointFilter<ValidationFilter<SkipEmployeeLoanInstallmentRequest>>();

api.MapPost("/payroll/loans/{loanId:guid}/settle-early", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid loanId,
    SettleEmployeeLoanRequest request,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var loan = await dbContext.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == loanId, cancellationToken);
    if (loan is null)
    {
        return Results.NotFound(new { error = "Loan not found." });
    }

    if (loan.Status is "Closed" or "Cancelled")
    {
        return Results.BadRequest(new { error = $"Loan cannot be settled when status is {loan.Status}." });
    }

    if (loan.RemainingBalance <= 0m)
    {
        loan.Status = "Closed";
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { loan.Id, loan.Status, loan.RemainingBalance });
    }

    var requestedAmount = request.Amount ?? loan.RemainingBalance;
    var settleAmount = Math.Round(Math.Min(requestedAmount, loan.RemainingBalance), 2);
    if (settleAmount <= 0m)
    {
        return Results.BadRequest(new { error = "Settlement amount must be greater than zero." });
    }

    var pendingInstallments = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loan.Id && x.Status == "Pending")
        .ToListAsync(cancellationToken);

    if (pendingInstallments.Count > 0)
    {
        var years = pendingInstallments.Select(x => x.Year).Distinct().ToList();
        var months = pendingInstallments.Select(x => x.Month).Distinct().ToList();
        var pendingPeriodKeys = pendingInstallments
            .Select(x => $"{x.Year:D4}-{x.Month:D2}")
            .ToHashSet(StringComparer.Ordinal);

        var blockedPeriods = await (from period in dbContext.PayrollPeriods
                                    join run in dbContext.PayrollRuns on period.Id equals run.PayrollPeriodId
                                    where years.Contains(period.Year) &&
                                          months.Contains(period.Month) &&
                                          run.Status == PayrollRunStatus.Locked
                                    select new { period.Year, period.Month })
            .Distinct()
            .ToListAsync(cancellationToken);

        var blockedKeys = blockedPeriods
            .Select(x => $"{x.Year:D4}-{x.Month:D2}")
            .Where(pendingPeriodKeys.Contains)
            .Distinct()
            .ToList();

        if (blockedKeys.Count > 0)
        {
            return Results.BadRequest(new { error = $"Cannot settle early while pending installments exist in locked periods: {string.Join(", ", blockedKeys)}." });
        }
    }

    var existingPeriodKeys = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loan.Id)
        .Select(x => $"{x.Year:D4}-{x.Month:D2}")
        .ToListAsync(cancellationToken);
    var existingPeriodSet = existingPeriodKeys.ToHashSet(StringComparer.Ordinal);

    var now = dateTimeProvider.UtcNow;
    var settlementYear = now.Year;
    var settlementMonth = now.Month;
    while (existingPeriodSet.Contains($"{settlementYear:D4}-{settlementMonth:D2}"))
    {
        settlementMonth++;
        if (settlementMonth > 12)
        {
            settlementMonth = 1;
            settlementYear++;
        }
    }

    dbContext.AddEntity(new EmployeeLoanInstallment
    {
        EmployeeLoanId = loan.Id,
        EmployeeId = loan.EmployeeId,
        Year = settlementYear,
        Month = settlementMonth,
        Amount = settleAmount,
        Status = "SettledEarly",
        DeductedAtUtc = now
    });

    loan.RemainingBalance = Math.Max(0m, Math.Round(loan.RemainingBalance - settleAmount, 2));
    loan.PaidInstallments = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loan.Id && (x.Status == "Deducted" || x.Status == "SettledEarly"))
        .CountAsync(cancellationToken) + 1;

    if (loan.RemainingBalance <= 0m)
    {
        foreach (var installment in pendingInstallments)
        {
            installment.Status = "Cancelled";
        }

        loan.Status = "Closed";
    }
    else if (loan.Status == "Draft")
    {
        loan.Status = "Active";
    }

    if (!string.IsNullOrWhiteSpace(request.Reason))
    {
        loan.Notes = string.IsNullOrWhiteSpace(loan.Notes)
            ? $"Early settlement: {request.Reason.Trim()}"
            : $"{loan.Notes}\nEarly settlement: {request.Reason.Trim()}";
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { loan.Id, loan.Status, loan.RemainingBalance });
})
    .AddEndpointFilter<ValidationFilter<SettleEmployeeLoanRequest>>();

api.MapGet("/payroll/loans/{loanId:guid}/installments", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid loanId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var loan = await dbContext.EmployeeLoans.FirstOrDefaultAsync(x => x.Id == loanId, cancellationToken);
    if (loan is null)
    {
        return Results.NotFound(new { error = "Loan not found." });
    }

    var rows = await dbContext.EmployeeLoanInstallments
        .Where(x => x.EmployeeLoanId == loanId)
        .OrderBy(x => x.Year)
        .ThenBy(x => x.Month)
        .Select(x => new
        {
            x.Id,
            x.Year,
            x.Month,
            x.Amount,
            x.Status,
            x.PayrollRunId,
            x.DeductedAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(rows);
});

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
    var shiftRules = await dbContext.ShiftRules.Where(x => x.IsActive).ToListAsync(cancellationToken);
    var shiftRuleById = shiftRules.ToDictionary(x => x.Id, x => x);
    var timesheets = await dbContext.TimesheetEntries
        .Where(x => x.WorkDate.Year == period.Year && x.WorkDate.Month == period.Month && x.Status == "Approved")
        .ToListAsync(cancellationToken);
    var allowancePolicies = await dbContext.AllowancePolicies
        .Where(x =>
            x.IsActive &&
            x.EffectiveFrom <= period.PeriodEndDate &&
            (x.EffectiveTo == null || x.EffectiveTo >= period.PeriodStartDate))
        .ToListAsync(cancellationToken);
    var activeLoanIds = await dbContext.EmployeeLoans
        .Where(x => x.Status == "Active")
        .Select(x => x.Id)
        .ToListAsync(cancellationToken);

    var loanInstallments = await dbContext.EmployeeLoanInstallments
        .Where(x =>
            x.Year == period.Year &&
            x.Month == period.Month &&
            x.Status == "Pending" &&
            activeLoanIds.Contains(x.EmployeeLoanId))
        .ToListAsync(cancellationToken);
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
        var approvedTimesheets = timesheets.Where(x => x.EmployeeId == employee.Id).ToList();
        var overtimeHours = approvedTimesheets.Count > 0
            ? approvedTimesheets.Sum(x => x.ApprovedOvertimeHours)
            : (employeeAttendance?.OvertimeHours ?? 0m);

        var policyAllowance = allowancePolicies
            .Where(x =>
                string.IsNullOrWhiteSpace(x.JobTitle) ||
                string.Equals(x.JobTitle.Trim(), employee.JobTitle.Trim(), StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.MonthlyAmount);

        var allowance = adjustments
            .Where(x => x.EmployeeId == employee.Id && x.Type == PayrollAdjustmentType.Allowance)
            .Sum(x => x.Amount) + policyAllowance;

        var manualDeduction = adjustments
            .Where(x => x.EmployeeId == employee.Id && x.Type == PayrollAdjustmentType.Deduction)
            .Sum(x => x.Amount);
        var loanDeduction = loanInstallments
            .Where(x => x.EmployeeId == employee.Id)
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

        // Saudi vs non-Saudi GOSI split:
        // Saudi: employee 9%, employer 11%
        // Non-Saudi: employee 0%, employer 2% (occupational hazard)
        var gosiEmployeeRate = employee.IsGosiEligible
            ? (employee.IsSaudiNational ? 0.09m : 0m)
            : 0m;
        var gosiEmployerRate = employee.IsGosiEligible
            ? (employee.IsSaudiNational ? 0.11m : 0.02m)
            : 0m;

        var gosiEmployeeContribution = Math.Round(gosiWageBase * gosiEmployeeRate, 2);
        var gosiEmployerContribution = Math.Round(gosiWageBase * gosiEmployerRate, 2);
        var totalDeductions = Math.Round(manualDeduction + loanDeduction + unpaidLeaveDeduction + gosiEmployeeContribution, 2);

        var overtimeAmount = 0m;
        if (approvedTimesheets.Count > 0)
        {
            var hourlyRate = employee.BaseSalary / 30m / 8m;
            overtimeAmount = Math.Round(approvedTimesheets.Sum(x =>
            {
                ShiftRule? shiftRule = null;
                if (x.ShiftRuleId.HasValue)
                {
                    shiftRuleById.TryGetValue(x.ShiftRuleId.Value, out shiftRule);
                }

                var multiplier = x.IsHoliday
                    ? (shiftRule?.OvertimeMultiplierHoliday ?? 2.5m)
                    : x.IsWeekend
                        ? (shiftRule?.OvertimeMultiplierWeekend ?? 2m)
                        : (shiftRule?.OvertimeMultiplierWeekday ?? 1.5m);

                return x.ApprovedOvertimeHours * hourlyRate * multiplier;
            }), 2);
        }
        else
        {
            var overtimeRate = (employee.BaseSalary / 30m / 8m) * 1.5m;
            overtimeAmount = Math.Round(overtimeHours * overtimeRate, 2);
        }
        var net = Math.Round(employee.BaseSalary + allowance + overtimeAmount - totalDeductions, 2);

        dbContext.AddEntity(new PayrollLine
        {
            PayrollRunId = run.Id,
            EmployeeId = employee.Id,
            BaseSalary = employee.BaseSalary,
            Allowances = allowance,
            ManualDeductions = manualDeduction,
            LoanDeduction = loanDeduction,
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
                     line.LoanDeduction,
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

api.MapGet("/payroll/runs/{runId:guid}/executive-summary", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    Guid runId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    if (run is null)
    {
        return Results.NotFound(new { error = "Payroll run not found." });
    }

    var period = await dbContext.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
    if (period is null)
    {
        return Results.NotFound(new { error = "Payroll period not found." });
    }

    var lines = await dbContext.PayrollLines
        .Where(x => x.PayrollRunId == runId)
        .Select(x => new
        {
            x.NetAmount,
            x.Deductions,
            x.OvertimeAmount,
            x.UnpaidLeaveDeduction
        })
        .ToListAsync(cancellationToken);

    if (lines.Count == 0)
    {
        return Results.BadRequest(new { error = "Payroll run has no lines." });
    }

    var profile = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    var currency = string.IsNullOrWhiteSpace(profile?.CurrencyCode) ? "SAR" : profile!.CurrencyCode.Trim().ToUpperInvariant();

    var totalNet = Math.Round(lines.Sum(x => x.NetAmount), 2);
    var totalDeductions = Math.Round(lines.Sum(x => x.Deductions), 2);
    var totalOvertime = Math.Round(lines.Sum(x => x.OvertimeAmount), 2);
    var totalUnpaidLeaveDeduction = Math.Round(lines.Sum(x => x.UnpaidLeaveDeduction), 2);
    var employeeCount = lines.Count;

    var previousMonthDate = new DateTime(period.Year, period.Month, 1).AddMonths(-1);
    var previousPeriod = await dbContext.PayrollPeriods
        .Where(x => x.Year == previousMonthDate.Year && x.Month == previousMonthDate.Month)
        .OrderByDescending(x => x.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    decimal previousTotalNet = 0m;
    var hasPrevious = false;
    if (previousPeriod is not null)
    {
        var previousRun = await dbContext.PayrollRuns
            .Where(x =>
                x.PayrollPeriodId == previousPeriod.Id &&
                (x.Status == PayrollRunStatus.Approved || x.Status == PayrollRunStatus.Locked))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (previousRun is not null)
        {
            previousTotalNet = await dbContext.PayrollLines
                .Where(x => x.PayrollRunId == previousRun.Id)
                .SumAsync(x => x.NetAmount, cancellationToken);
            previousTotalNet = Math.Round(previousTotalNet, 2);
            hasPrevious = true;
        }
    }

    var deltaAmount = Math.Round(totalNet - previousTotalNet, 2);
    var deltaPercent = hasPrevious && previousTotalNet != 0m
        ? Math.Round((deltaAmount / previousTotalNet) * 100m, 2)
        : 0m;

    var trendEn = !hasPrevious
        ? "with no approved previous-month baseline."
        : deltaAmount > 0m
            ? $"up {deltaPercent:F2}% from last month."
            : deltaAmount < 0m
                ? $"down {Math.Abs(deltaPercent):F2}% from last month."
                : "flat versus last month.";

    var trendAr = !hasPrevious
        ? "ولا يوجد خط أساس معتمد للشهر السابق."
        : deltaAmount > 0m
            ? $"بارتفاع {deltaPercent:F2}% عن الشهر السابق."
            : deltaAmount < 0m
                ? $"بانخفاض {Math.Abs(deltaPercent):F2}% عن الشهر السابق."
                : "ومستقر مقارنة بالشهر السابق.";

    var summaryEn =
        $"Payroll {period.Year}-{period.Month:00} includes {employeeCount} employee(s). " +
        $"Net payroll is {totalNet:F2} {currency}, {trendEn} " +
        $"Overtime impact: {totalOvertime:F2} {currency}; total deductions: {totalDeductions:F2} {currency}; " +
        $"unpaid leave deduction: {totalUnpaidLeaveDeduction:F2} {currency}.";

    var summaryAr =
        $"رواتب {period.Year}-{period.Month:00} تشمل {employeeCount} موظف/موظفة. " +
        $"صافي الرواتب {totalNet:F2} {currency}، {trendAr} " +
        $"أثر العمل الإضافي: {totalOvertime:F2} {currency}؛ إجمالي الخصومات: {totalDeductions:F2} {currency}؛ " +
        $"خصم الإجازات غير المدفوعة: {totalUnpaidLeaveDeduction:F2} {currency}.";

    return Results.Ok(new
    {
        runId = run.Id,
        periodYear = period.Year,
        periodMonth = period.Month,
        employeeCount,
        currencyCode = currency,
        totalNet,
        totalDeductions,
        totalOvertime,
        totalUnpaidLeaveDeduction,
        previousTotalNet = hasPrevious ? previousTotalNet : (decimal?)null,
        deltaAmount = hasPrevious ? deltaAmount : (decimal?)null,
        deltaPercent = hasPrevious ? deltaPercent : (decimal?)null,
        summaryEn,
        summaryAr
    });
});

api.MapGet("/payroll/runs/{runId:guid}/pre-approval-checks", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
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

    var findings = await BuildPayrollPreApprovalFindingsAsync(dbContext, run, cancellationToken);
    var hasBlockingFindings = findings.Any(x => string.Equals(x.Severity, "Critical", StringComparison.OrdinalIgnoreCase));

    return Results.Ok(new
    {
        runId = run.Id,
        hasBlockingFindings,
        generatedAtUtc = dateTimeProvider.UtcNow,
        findings
    });
});

api.MapPost("/payroll/runs/{runId:guid}/approve", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin)] async (
    Guid runId,
    HttpContext httpContext,
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

    var findings = await BuildPayrollPreApprovalFindingsAsync(dbContext, run, cancellationToken);
    var blockingFindings = findings.Where(x => string.Equals(x.Severity, "Critical", StringComparison.OrdinalIgnoreCase)).ToList();
    if (blockingFindings.Count > 0)
    {
        return Results.BadRequest(new
        {
            error = "Approval blocked by critical payroll findings.",
            findings = blockingFindings
        });
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var warningCount = findings.Count(x => string.Equals(x.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
    var findingsSnapshot = EncodeApprovalFindingsSnapshot(findings);
    dbContext.AddEntity(new AuditLog
    {
        TenantId = run.TenantId,
        UserId = userId,
        Method = "PAYROLL_APPROVE_STANDARD",
        Path = $"/api/payroll/runs/{runId}/approve?critical=0&warning={warningCount}&snapshot={findingsSnapshot}",
        StatusCode = StatusCodes.Status200OK,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        DurationMs = 0
    });

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { run.Id, run.Status });
});

api.MapPost("/payroll/runs/{runId:guid}/approve-override", [Authorize(Roles = RoleNames.Owner)] async (
    Guid runId,
    ApprovePayrollRunOverrideRequest request,
    HttpContext httpContext,
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

    var findings = await BuildPayrollPreApprovalFindingsAsync(dbContext, run, cancellationToken);
    var blockingFindings = findings.Where(x => string.Equals(x.Severity, "Critical", StringComparison.OrdinalIgnoreCase)).ToList();
    if (blockingFindings.Count == 0)
    {
        return Results.BadRequest(new { error = "No critical findings found. Use standard approve endpoint." });
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var reason = request.Reason.Trim();
    var compactReason = reason.Length <= 220 ? reason : reason[..220];
    var encodedReason = Uri.EscapeDataString(compactReason);
    var category = request.Category.Trim();
    var encodedCategory = Uri.EscapeDataString(category);
    var referenceId = request.ReferenceId.Trim();
    var compactReferenceId = referenceId.Length <= 80 ? referenceId : referenceId[..80];
    var encodedReferenceId = Uri.EscapeDataString(compactReferenceId);
    var nowUtc = dateTimeProvider.UtcNow;
    var monthKey = $"{nowUtc:yyyyMM}";
    var monthStartUtc = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    var monthEndUtc = monthStartUtc.AddMonths(1);

    var existingReferencePaths = await dbContext.AuditLogs
        .Where(x =>
            x.Method == "PAYROLL_APPROVE_OVERRIDE" &&
            x.CreatedAtUtc >= monthStartUtc &&
            x.CreatedAtUtc < monthEndUtc)
        .Select(x => x.Path)
        .ToListAsync(cancellationToken);

    if (!TryParseOverrideReferenceId(compactReferenceId, out var referenceMonthKey, out _))
    {
        var suggestedReferenceId = BuildNextOverrideReferenceId(monthKey, existingReferencePaths);
        return Results.BadRequest(new
        {
            error = "Reference id format is invalid. Expected OVR-YYYYMM-####.",
            suggestedReferenceId
        });
    }

    if (!string.Equals(referenceMonthKey, monthKey, StringComparison.Ordinal))
    {
        var suggestedReferenceId = BuildNextOverrideReferenceId(monthKey, existingReferencePaths);
        return Results.BadRequest(new
        {
            error = $"Reference id month must match current month ({monthKey}).",
            suggestedReferenceId
        });
    }

    var duplicateReferenceExists = existingReferencePaths.Any(path =>
        string.Equals(ReadAuditQueryValue(path, "referenceId"), compactReferenceId, StringComparison.OrdinalIgnoreCase));

    if (duplicateReferenceExists)
    {
        var suggestedReferenceId = BuildNextOverrideReferenceId(monthKey, existingReferencePaths);
        return Results.BadRequest(new
        {
            error = $"Reference id '{compactReferenceId}' already used in {nowUtc:yyyy-MM}. Use a unique reference id.",
            suggestedReferenceId
        });
    }

    run.Status = PayrollRunStatus.Approved;
    run.ApprovedAtUtc = nowUtc;

    var period = await dbContext.PayrollPeriods.FirstAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
    period.Status = PayrollRunStatus.Approved;

    var criticalCodes = string.Join(",", blockingFindings.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase));
    var findingsSnapshot = EncodeApprovalFindingsSnapshot(findings);
    dbContext.AddEntity(new AuditLog
    {
        TenantId = run.TenantId,
        UserId = userId,
        Method = "PAYROLL_APPROVE_OVERRIDE",
        Path = $"/api/payroll/runs/{runId}/approve-override?critical={criticalCodes}&category={encodedCategory}&referenceId={encodedReferenceId}&reason={encodedReason}&snapshot={findingsSnapshot}",
        StatusCode = StatusCodes.Status200OK,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        DurationMs = 0
    });

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new
    {
        run.Id,
        run.Status,
        overrideApproved = true,
        criticalFindingsCount = blockingFindings.Count
    });
})
    .AddEndpointFilter<ValidationFilter<ApprovePayrollRunOverrideRequest>>();

api.MapGet("/payroll/runs/{runId:guid}/approval-decisions", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr)] async (
    Guid runId,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var run = await dbContext.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    if (run is null)
    {
        return Results.NotFound();
    }

    var decisions = await dbContext.AuditLogs
        .Where(x =>
            (x.Method == "PAYROLL_APPROVE_STANDARD" || x.Method == "PAYROLL_APPROVE_OVERRIDE") &&
            x.Path.Contains($"/api/payroll/runs/{runId}/", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.CreatedAtUtc,
            x.Method,
            x.UserId,
            x.Path
        })
        .ToListAsync(cancellationToken);

    static string ReadQueryValue(string path, string key)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var questionMarkIndex = path.IndexOf('?');
        if (questionMarkIndex < 0 || questionMarkIndex + 1 >= path.Length)
        {
            return string.Empty;
        }

        var query = path[(questionMarkIndex + 1)..];
        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length != 2)
            {
                continue;
            }

            if (string.Equals(keyValue[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(keyValue[1]);
            }
        }

        return string.Empty;
    }

    var items = decisions.Select(x =>
    {
        var snapshotJson = ReadQueryValue(x.Path, "snapshot");
        var findingsCount = 0;
        if (!string.IsNullOrWhiteSpace(snapshotJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(snapshotJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    findingsCount = doc.RootElement.GetArrayLength();
                }
            }
            catch
            {
                findingsCount = 0;
            }
        }

        return new
        {
            x.Id,
            x.CreatedAtUtc,
            DecisionType = x.Method == "PAYROLL_APPROVE_OVERRIDE" ? "Override" : "Standard",
            x.UserId,
            Category = ReadQueryValue(x.Path, "category"),
            ReferenceId = ReadQueryValue(x.Path, "referenceId"),
            Reason = ReadQueryValue(x.Path, "reason"),
            CriticalCodes = ReadQueryValue(x.Path, "critical"),
            WarningCount = ReadQueryValue(x.Path, "warning"),
            FindingsSnapshotJson = snapshotJson,
            FindingsCount = findingsCount
        };
    });

    return Results.Ok(new { items });
});

api.MapGet("/payroll/governance/overview", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    int? days,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var windowDays = Math.Clamp(days ?? 30, 1, 180);
    var fromUtc = dateTimeProvider.UtcNow.AddDays(-windowDays);

    var decisions = await dbContext.AuditLogs
        .Where(x =>
            x.CreatedAtUtc >= fromUtc &&
            (x.Method == "PAYROLL_APPROVE_STANDARD" || x.Method == "PAYROLL_APPROVE_OVERRIDE"))
        .Select(x => new
        {
            x.Method,
            x.Path
        })
        .ToListAsync(cancellationToken);

    var totalApprovals = decisions.Count;
    var overrideApprovals = decisions.Count(x => x.Method == "PAYROLL_APPROVE_OVERRIDE");
    var standardApprovals = totalApprovals - overrideApprovals;
    var overrideRatePercent = totalApprovals == 0 ? 0m : Math.Round(overrideApprovals * 100m / totalApprovals, 1);

    var criticalCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var overrideCategoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var criticalDecisionCount = 0;
    var overridesWithReference = 0;
    var overridesWithLongReason = 0;

    foreach (var decision in decisions)
    {
        if (decision.Method == "PAYROLL_APPROVE_OVERRIDE")
        {
            var categoryRaw = string.IsNullOrWhiteSpace(decision.Path)
                ? string.Empty
                : ReadAuditQueryValue(decision.Path, "category");
            var category = string.IsNullOrWhiteSpace(categoryRaw) ? "Unspecified" : categoryRaw.Trim();
            overrideCategoryCounts.TryGetValue(category, out var categoryCount);
            overrideCategoryCounts[category] = categoryCount + 1;

            var referenceId = string.IsNullOrWhiteSpace(decision.Path)
                ? string.Empty
                : ReadAuditQueryValue(decision.Path, "referenceId");
            if (!string.IsNullOrWhiteSpace(referenceId))
            {
                overridesWithReference++;
            }

            var reason = string.IsNullOrWhiteSpace(decision.Path)
                ? string.Empty
                : ReadAuditQueryValue(decision.Path, "reason");
            if (!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= 30)
            {
                overridesWithLongReason++;
            }
        }

        var criticalCodesRaw = string.IsNullOrWhiteSpace(decision.Path)
            ? string.Empty
            : ReadAuditQueryValue(decision.Path, "critical");

        if (string.IsNullOrWhiteSpace(criticalCodesRaw) || criticalCodesRaw == "0")
        {
            continue;
        }

        criticalDecisionCount++;
        var parts = criticalCodesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var code = part.Trim();
            if (string.IsNullOrWhiteSpace(code) || code == "0")
            {
                continue;
            }

            criticalCounts.TryGetValue(code, out var count);
            criticalCounts[code] = count + 1;
        }
    }

    var topCriticalCodes = criticalCounts
        .OrderByDescending(x => x.Value)
        .ThenBy(x => x.Key)
        .Take(5)
        .Select(x => new
        {
            code = x.Key,
            count = x.Value
        })
        .ToList();

    var topOverrideCategories = overrideCategoryCounts
        .OrderByDescending(x => x.Value)
        .ThenBy(x => x.Key)
        .Take(5)
        .Select(x => new
        {
            category = x.Key,
            count = x.Value
        })
        .ToList();

    var overrideReferenceCoveragePercent = overrideApprovals == 0
        ? 0m
        : Math.Round(overridesWithReference * 100m / overrideApprovals, 1);
    var overrideDocumentationQualityPercent = overrideApprovals == 0
        ? 0m
        : Math.Round(((overridesWithReference + overridesWithLongReason) / (2m * overrideApprovals)) * 100m, 1);

    return Results.Ok(new
    {
        windowDays,
        totalApprovals,
        standardApprovals,
        overrideApprovals,
        overrideRatePercent,
        overridesWithReference,
        overrideReferenceCoveragePercent,
        overrideDocumentationQualityPercent,
        criticalDecisionCount,
        topCriticalCodes,
        topOverrideCategories
    });
});

api.MapGet("/payroll/governance/trend", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    int? months,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var monthsWindow = Math.Clamp(months ?? 6, 3, 12);
    var now = dateTimeProvider.UtcNow;
    var firstMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-(monthsWindow - 1));

    try
    {
        var decisions = await dbContext.AuditLogs
            .Where(x =>
                x.CreatedAtUtc >= firstMonth &&
                (x.Method == "PAYROLL_APPROVE_STANDARD" || x.Method == "PAYROLL_APPROVE_OVERRIDE"))
            .Select(x => new
            {
                x.CreatedAtUtc,
                x.Method
            })
            .ToListAsync(cancellationToken);

        var byMonth = decisions
            .GroupBy(x => $"{x.CreatedAtUtc.Year:D4}-{x.CreatedAtUtc.Month:D2}")
            .ToDictionary(g => g.Key, g => new
            {
                total = g.Count(),
                overrides = g.Count(x => x.Method == "PAYROLL_APPROVE_OVERRIDE")
            });

        var items = new List<object>(monthsWindow);
        for (var i = 0; i < monthsWindow; i++)
        {
            var monthDate = firstMonth.AddMonths(i);
            var key = $"{monthDate.Year:D4}-{monthDate.Month:D2}";

            var total = byMonth.TryGetValue(key, out var row) ? row.total : 0;
            var overrides = byMonth.TryGetValue(key, out row) ? row.overrides : 0;
            var rate = total == 0 ? 0m : Math.Round(overrides * 100m / total, 1);

            items.Add(new
            {
                month = key,
                totalApprovals = total,
                overrideApprovals = overrides,
                overrideRatePercent = rate
            });
        }

        return Results.Ok(new
        {
            monthsWindow,
            items
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to build payroll governance trend. Returning empty trend as fallback.");

        var items = Enumerable.Range(0, monthsWindow)
            .Select(i =>
            {
                var monthDate = firstMonth.AddMonths(i);
                return new
                {
                    month = $"{monthDate.Year:D4}-{monthDate.Month:D2}",
                    totalApprovals = 0,
                    overrideApprovals = 0,
                    overrideRatePercent = 0m
                };
            })
            .ToList();

        return Results.Ok(new
        {
            monthsWindow,
            items,
            degraded = true
        });
    }
});

api.MapGet("/payroll/governance/decisions", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    int? days,
    string? criticalCode,
    string? referenceId,
    string? category,
    Guid? runId,
    int? skip,
    int? take,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var windowDays = Math.Clamp(days ?? 30, 1, 180);
    var safeSkip = Math.Max(0, skip ?? 0);
    var maxRows = Math.Clamp(take ?? 100, 1, 200);
    var fromUtc = dateTimeProvider.UtcNow.AddDays(-windowDays);
    var normalizedCriticalCode = string.IsNullOrWhiteSpace(criticalCode) ? null : criticalCode.Trim();
    var normalizedReferenceId = string.IsNullOrWhiteSpace(referenceId) ? null : referenceId.Trim();
    var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

    var decisions = await dbContext.AuditLogs
        .Where(x =>
            x.CreatedAtUtc >= fromUtc &&
            (x.Method == "PAYROLL_APPROVE_STANDARD" || x.Method == "PAYROLL_APPROVE_OVERRIDE"))
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.CreatedAtUtc,
            x.Method,
            x.UserId,
            x.Path
        })
        .ToListAsync(cancellationToken);

    var filtered = decisions
        .Select(x =>
        {
            var criticalCodes = ReadAuditQueryValue(x.Path, "critical");
            var category = ReadAuditQueryValue(x.Path, "category");
            var referenceId = ReadAuditQueryValue(x.Path, "referenceId");
            var reason = ReadAuditQueryValue(x.Path, "reason");
            var warningCount = ReadAuditQueryValue(x.Path, "warning");
            var runId = TryReadRunIdFromAuditPath(x.Path);
            return new
            {
                x.Id,
                x.CreatedAtUtc,
                x.Method,
                x.UserId,
                CriticalCodes = criticalCodes,
                Category = category,
                ReferenceId = referenceId,
                Reason = reason,
                WarningCount = warningCount,
                RunId = runId
            };
        })
        .Where(x =>
        {
            if (normalizedCriticalCode is null)
            {
                return true;
            }

            var codes = x.CriticalCodes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!codes.Any(code => string.Equals(code, normalizedCriticalCode, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        })
        .Where(x =>
        {
            if (normalizedReferenceId is null)
            {
                return true;
            }

            return string.Equals(x.ReferenceId, normalizedReferenceId, StringComparison.OrdinalIgnoreCase);
        })
        .Where(x =>
        {
            if (normalizedCategory is null)
            {
                return true;
            }

            return string.Equals(x.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase);
        })
        .Where(x =>
        {
            if (!runId.HasValue || runId.Value == Guid.Empty)
            {
                return true;
            }

            return x.RunId.HasValue && x.RunId.Value == runId.Value;
        })
        .ToList();

    var total = filtered.Count;
    var paged = filtered
        .Skip(safeSkip)
        .Take(maxRows)
        .Select(x => new
        {
            x.Id,
            x.CreatedAtUtc,
            DecisionType = x.Method == "PAYROLL_APPROVE_OVERRIDE" ? "Override" : "Standard",
            x.UserId,
            x.RunId,
            x.CriticalCodes,
            x.Category,
            x.ReferenceId,
            x.WarningCount,
            x.Reason
        })
        .ToList();

    return Results.Ok(new
    {
        windowDays,
        criticalCode = normalizedCriticalCode ?? string.Empty,
        referenceId = normalizedReferenceId ?? string.Empty,
        category = normalizedCategory ?? string.Empty,
        runId = runId?.ToString() ?? string.Empty,
        total,
        skip = safeSkip,
        take = maxRows,
        hasMore = safeSkip + paged.Count < total,
        items = paged
    });
});

api.MapGet("/payroll/governance/decisions/export-csv", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    int? days,
    string? criticalCode,
    string? referenceId,
    string? category,
    Guid? runId,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var windowDays = Math.Clamp(days ?? 30, 1, 180);
    var fromUtc = dateTimeProvider.UtcNow.AddDays(-windowDays);
    var normalizedCriticalCode = string.IsNullOrWhiteSpace(criticalCode) ? null : criticalCode.Trim();
    var normalizedReferenceId = string.IsNullOrWhiteSpace(referenceId) ? null : referenceId.Trim();
    var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();

    var decisions = await dbContext.AuditLogs
        .Where(x =>
            x.CreatedAtUtc >= fromUtc &&
            (x.Method == "PAYROLL_APPROVE_STANDARD" || x.Method == "PAYROLL_APPROVE_OVERRIDE"))
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new
        {
            x.Id,
            x.CreatedAtUtc,
            x.Method,
            x.UserId,
            x.Path
        })
        .ToListAsync(cancellationToken);

    var filtered = decisions
        .Select(x =>
        {
            var parsedRunId = TryReadRunIdFromAuditPath(x.Path);
            return new
            {
                x.Id,
                x.CreatedAtUtc,
                DecisionType = x.Method == "PAYROLL_APPROVE_OVERRIDE" ? "Override" : "Standard",
                x.UserId,
                RunId = parsedRunId,
                CriticalCodes = ReadAuditQueryValue(x.Path, "critical"),
                Category = ReadAuditQueryValue(x.Path, "category"),
                ReferenceId = ReadAuditQueryValue(x.Path, "referenceId"),
                WarningCount = ReadAuditQueryValue(x.Path, "warning"),
                Reason = ReadAuditQueryValue(x.Path, "reason")
            };
        })
        .Where(x =>
        {
            if (normalizedCriticalCode is null)
            {
                return true;
            }

            var codes = x.CriticalCodes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return codes.Any(code => string.Equals(code, normalizedCriticalCode, StringComparison.OrdinalIgnoreCase));
        })
        .Where(x => normalizedReferenceId is null || string.Equals(x.ReferenceId, normalizedReferenceId, StringComparison.OrdinalIgnoreCase))
        .Where(x => normalizedCategory is null || string.Equals(x.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
        .Where(x => !runId.HasValue || runId.Value == Guid.Empty || (x.RunId.HasValue && x.RunId.Value == runId.Value))
        .ToList();

    static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    var csv = new StringBuilder();
    csv.AppendLine("Report,ReferenceRegistry");
    csv.AppendLine($"GeneratedAtUtc,{dateTimeProvider.UtcNow:yyyy-MM-dd HH:mm:ss}");
    csv.AppendLine($"WindowDays,{windowDays}");
    csv.AppendLine($"FilterCriticalCode,{EscapeCsv(normalizedCriticalCode ?? "")}");
    csv.AppendLine($"FilterReferenceId,{EscapeCsv(normalizedReferenceId ?? "")}");
    csv.AppendLine($"FilterCategory,{EscapeCsv(normalizedCategory ?? "")}");
    csv.AppendLine($"FilterRunId,{EscapeCsv(runId?.ToString() ?? "")}");
    csv.AppendLine($"TotalRows,{filtered.Count}");
    csv.AppendLine();
    csv.AppendLine("CreatedAtUtc,DecisionType,Category,ReferenceId,RunId,CriticalCodes,WarningCount,Reason,UserId");

    foreach (var row in filtered)
    {
        csv.AppendLine(
            $"{row.CreatedAtUtc:yyyy-MM-dd HH:mm:ss},{EscapeCsv(row.DecisionType)},{EscapeCsv(row.Category ?? "")},{EscapeCsv(row.ReferenceId ?? "")},{EscapeCsv(row.RunId?.ToString() ?? "")},{EscapeCsv(row.CriticalCodes ?? "")},{EscapeCsv(row.WarningCount ?? "")},{EscapeCsv(row.Reason ?? "")},{EscapeCsv(row.UserId?.ToString() ?? "")}");
    }

    var fileName = $"reference-registry-{dateTimeProvider.UtcNow:yyyyMMdd-HHmmss}.csv";
    var fileBytes = Encoding.UTF8.GetBytes(csv.ToString());
    return Results.File(fileBytes, "text/csv", fileName);
});

api.MapGet("/payroll/governance/next-reference-id", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var nowUtc = dateTimeProvider.UtcNow;
    var monthStartUtc = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    var monthEndUtc = monthStartUtc.AddMonths(1);
    var monthKey = $"{nowUtc:yyyyMM}";
    var prefix = $"OVR-{monthKey}-";

    var referenceIds = await dbContext.AuditLogs
        .Where(x =>
            x.Method == "PAYROLL_APPROVE_OVERRIDE" &&
            x.CreatedAtUtc >= monthStartUtc &&
            x.CreatedAtUtc < monthEndUtc)
        .Select(x => x.Path)
        .ToListAsync(cancellationToken);

    var nextReferenceId = BuildNextOverrideReferenceId(monthKey, referenceIds);
    var isSequenceExhausted = nextReferenceId.EndsWith("-9999", StringComparison.Ordinal);
    _ = TryParseOverrideReferenceId(nextReferenceId, out _, out var nextSequence);

    return Results.Ok(new
    {
        monthKey,
        nextSequence,
        referenceId = nextReferenceId,
        isSequenceExhausted
    });
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

    var lineEmployeeIds = await dbContext.PayrollLines
        .Where(x => x.PayrollRunId == run.Id)
        .Select(x => x.EmployeeId)
        .Distinct()
        .ToListAsync(cancellationToken);

    var activeLoanIds = await dbContext.EmployeeLoans
        .Where(x => x.Status == "Active")
        .Select(x => x.Id)
        .ToListAsync(cancellationToken);

    var installments = await dbContext.EmployeeLoanInstallments
        .Where(x =>
            x.Year == period.Year &&
            x.Month == period.Month &&
            x.Status == "Pending" &&
            activeLoanIds.Contains(x.EmployeeLoanId) &&
            lineEmployeeIds.Contains(x.EmployeeId))
        .ToListAsync(cancellationToken);

    if (installments.Count > 0)
    {
        var loanIds = installments.Select(x => x.EmployeeLoanId).Distinct().ToList();
        var loans = await dbContext.EmployeeLoans.Where(x => loanIds.Contains(x.Id)).ToListAsync(cancellationToken);

        foreach (var installment in installments)
        {
            installment.Status = "Deducted";
            installment.DeductedAtUtc = dateTimeProvider.UtcNow;
            installment.PayrollRunId = run.Id;
        }

        foreach (var loan in loans)
        {
            var deductedAmount = installments.Where(x => x.EmployeeLoanId == loan.Id).Sum(x => x.Amount);
            loan.RemainingBalance = Math.Max(0m, Math.Round(loan.RemainingBalance - deductedAmount, 2));
            loan.PaidInstallments += installments.Count(x => x.EmployeeLoanId == loan.Id);

            if (loan.RemainingBalance <= 0m || loan.PaidInstallments >= loan.TotalInstallments)
            {
                loan.Status = "Closed";
            }
            else if (loan.Status == "Draft")
            {
                loan.Status = "Active";
            }
        }
    }

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

    if (run.Status is not PayrollRunStatus.Approved and not PayrollRunStatus.Locked)
    {
        return Results.BadRequest(new
        {
            error = "Payroll register export requires payroll run status Approved or Locked.",
            runStatus = run.Status.ToString()
        });
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

    if (run.Status is not PayrollRunStatus.Approved and not PayrollRunStatus.Locked)
    {
        return Results.BadRequest(new
        {
            error = "GOSI export requires payroll run status Approved or Locked.",
            runStatus = run.Status.ToString()
        });
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

    if (run.Status is not PayrollRunStatus.Approved and not PayrollRunStatus.Locked)
    {
        return Results.BadRequest(new
        {
            error = "WPS export requires payroll run status Approved or Locked.",
            runStatus = run.Status.ToString()
        });
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

    if (run.Status is not PayrollRunStatus.Approved and not PayrollRunStatus.Locked)
    {
        return Results.BadRequest(new
        {
            error = "Payslip export requires payroll run status Approved or Locked.",
            runStatus = run.Status.ToString()
        });
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

api.MapGet("/smart-alerts", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    int? daysAhead,
    IApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    var safeDaysAhead = Math.Clamp(daysAhead ?? 30, 1, 120);
    var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow.Date);
    var alerts = new List<SmartAlertResponse>();

    var company = await dbContext.CompanyProfiles.FirstOrDefaultAsync(cancellationToken);
    var defaultPayDay = company?.DefaultPayDay is >= 1 and <= 31 ? company.DefaultPayDay : 25;

    var employees = await dbContext.Employees
        .Select(x => new
        {
            x.Id,
            x.FirstName,
            x.LastName,
            x.StartDate,
            x.ContractEndDate,
            x.IsSaudiNational,
            x.EmployeeNumber,
            x.BankIban,
            x.IsGosiEligible,
            x.GosiBasicWage
        })
        .ToListAsync(cancellationToken);

    foreach (var employee in employees)
    {
        var probationEnd = employee.StartDate.AddDays(89);
        var daysLeft = probationEnd.DayNumber - today.DayNumber;
        if (daysLeft < 0 || daysLeft > safeDaysAhead)
        {
            continue;
        }

        var severity = daysLeft <= 7 ? "Critical" : daysLeft <= 30 ? "Warning" : "Notice";
        alerts.Add(new SmartAlertResponse(
            $"PROBATION:{employee.Id:N}:{probationEnd:yyyyMMdd}",
            "ProbationEndingSoon",
            severity,
            $"{employee.FirstName} {employee.LastName}",
            $"Probation ends on {probationEnd:yyyy-MM-dd} ({daysLeft} day(s) left).",
            daysLeft,
            probationEnd.ToString("yyyy-MM-dd")));
    }

    foreach (var employee in employees)
    {
        if (!employee.ContractEndDate.HasValue)
        {
            continue;
        }

        var daysLeft = employee.ContractEndDate.Value.DayNumber - today.DayNumber;
        if (daysLeft < 0 || daysLeft > safeDaysAhead)
        {
            continue;
        }

        var severity = daysLeft <= 7 ? "Critical" : daysLeft <= 30 ? "Warning" : "Notice";
        alerts.Add(new SmartAlertResponse(
            $"CONTRACT_END:{employee.Id:N}:{employee.ContractEndDate.Value:yyyyMMdd}",
            "ContractEndingSoon",
            severity,
            $"{employee.FirstName} {employee.LastName}",
            $"Employment contract ends on {employee.ContractEndDate.Value:yyyy-MM-dd} ({daysLeft} day(s) left).",
            daysLeft,
            employee.ContractEndDate.Value.ToString("yyyy-MM-dd")));
    }

    foreach (var employee in employees)
    {
        var fullName = $"{employee.FirstName} {employee.LastName}".Trim();
        var employeeRef = string.IsNullOrWhiteSpace(employee.EmployeeNumber)
            ? employee.Id.ToString("N")
            : employee.EmployeeNumber.Trim();

        if (string.IsNullOrWhiteSpace(employee.BankIban))
        {
            alerts.Add(new SmartAlertResponse(
                $"MISSING_IBAN:{employeeRef}",
                "MissingIban",
                "Warning",
                fullName,
                "Employee bank IBAN is missing. Complete payment profile before payroll/WPS export.",
                null,
                null));
        }

        if (employee.IsGosiEligible && employee.GosiBasicWage <= 0m)
        {
            alerts.Add(new SmartAlertResponse(
                $"MISSING_GOSI_SETUP:{employeeRef}",
                "MissingGosiSetup",
                "Critical",
                fullName,
                "Employee is marked GOSI eligible but GOSI basic wage is zero. Fix before payroll approval.",
                null,
                null));
        }

        if (!employee.IsSaudiNational && !employee.ContractEndDate.HasValue)
        {
            alerts.Add(new SmartAlertResponse(
                $"MISSING_CONTRACT_END:{employeeRef}",
                "MissingContractEndDate",
                "Warning",
                fullName,
                "Contract end date is missing. Add it to track renewal and expiry risk.",
                null,
                null));
        }
    }

    var complianceAlerts = await dbContext.ComplianceAlerts
        .Where(x => !x.IsResolved && x.DaysLeft >= 0 && x.DaysLeft <= safeDaysAhead)
        .OrderBy(x => x.DaysLeft)
        .Select(x => new
        {
            x.Id,
            x.EmployeeName,
            x.DocumentType,
            x.Severity,
            x.DaysLeft,
            x.ExpiryDate
        })
        .ToListAsync(cancellationToken);

    foreach (var alert in complianceAlerts)
    {
        alerts.Add(new SmartAlertResponse(
            $"DOC:{alert.Id:N}",
            "DocumentExpiry",
            string.IsNullOrWhiteSpace(alert.Severity) ? "Warning" : alert.Severity,
            alert.EmployeeName,
            $"{alert.DocumentType} expires on {alert.ExpiryDate:yyyy-MM-dd} ({alert.DaysLeft} day(s) left).",
            alert.DaysLeft,
            alert.ExpiryDate.ToString("yyyy-MM-dd")));
    }

    var now = dateTimeProvider.UtcNow;
    var currentYear = now.Year;
    var currentMonth = now.Month;
    var period = await dbContext.PayrollPeriods
        .FirstOrDefaultAsync(x => x.Year == currentYear && x.Month == currentMonth, cancellationToken);

    var payrollAlertLeadDays = 5;
    var dayToStartAlert = Math.Max(1, defaultPayDay - payrollAlertLeadDays);
    if (now.Day >= dayToStartAlert)
    {
        var payrollAlertKey = $"PAYROLL_PENDING:{currentYear}{currentMonth:00}";
        if (period is null)
        {
            var daysLeft = Math.Max(0, defaultPayDay - now.Day);
            var severity = now.Day >= defaultPayDay ? "Critical" : "Warning";
            alerts.Add(new SmartAlertResponse(
                payrollAlertKey,
                "PayrollApprovalPending",
                severity,
                null,
                $"Payroll period for {currentYear}-{currentMonth:00} is not created yet. Default pay day is {defaultPayDay}.",
                daysLeft,
                new DateOnly(currentYear, currentMonth, Math.Min(defaultPayDay, DateTime.DaysInMonth(currentYear, currentMonth))).ToString("yyyy-MM-dd")));
        }
        else
        {
            var run = await dbContext.PayrollRuns
                .Where(x => x.PayrollPeriodId == period.Id)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var isApprovedOrLocked = run is not null && (run.Status == PayrollRunStatus.Approved || run.Status == PayrollRunStatus.Locked);
            if (!isApprovedOrLocked)
            {
                var daysLeft = Math.Max(0, defaultPayDay - now.Day);
                var severity = now.Day >= defaultPayDay ? "Critical" : "Warning";
                alerts.Add(new SmartAlertResponse(
                    payrollAlertKey,
                    "PayrollApprovalPending",
                    severity,
                    null,
                    $"Payroll for {currentYear}-{currentMonth:00} is not approved yet. Default pay day is {defaultPayDay}.",
                    daysLeft,
                    new DateOnly(currentYear, currentMonth, Math.Min(defaultPayDay, DateTime.DaysInMonth(currentYear, currentMonth))).ToString("yyyy-MM-dd")));
            }
        }
    }

    var currentRun = period is null
        ? null
        : await dbContext.PayrollRuns
            .Where(x => x.PayrollPeriodId == period.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    var payrollApprovedOrLocked = currentRun is not null &&
        (currentRun.Status == PayrollRunStatus.Approved || currentRun.Status == PayrollRunStatus.Locked);

    if (!payrollApprovedOrLocked)
    {
        var overtimeRows = await (from att in dbContext.AttendanceInputs
                                  join employee in dbContext.Employees on att.EmployeeId equals employee.Id
                                  where att.Year == currentYear && att.Month == currentMonth && att.OvertimeHours >= 8m
                                  select new
                                  {
                                      employee.Id,
                                      employee.FirstName,
                                      employee.LastName,
                                      att.OvertimeHours
                                  })
            .OrderByDescending(x => x.OvertimeHours)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var row in overtimeRows)
        {
            var severity = row.OvertimeHours >= 30m ? "Critical" : row.OvertimeHours >= 16m ? "Warning" : "Notice";
            var daysLeft = Math.Max(0, defaultPayDay - now.Day);
            alerts.Add(new SmartAlertResponse(
                $"OVERTIME_PENDING:{row.Id:N}:{currentYear}{currentMonth:00}",
                "OvertimePendingReview",
                severity,
                $"{row.FirstName} {row.LastName}",
                $"Overtime input is {row.OvertimeHours:F2} hour(s) for {currentYear}-{currentMonth:00}. Review before payroll approval.",
                daysLeft,
                new DateOnly(currentYear, currentMonth, Math.Min(defaultPayDay, DateTime.DaysInMonth(currentYear, currentMonth))).ToString("yyyy-MM-dd")));
        }
    }

    var actionPaths = await dbContext.AuditLogs
        .Where(x => x.Method == "SMART_ALERT_ACK" || x.Method == "SMART_ALERT_SNOOZE")
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new { x.Method, x.Path, x.CreatedAtUtc })
        .Take(3000)
        .ToListAsync(cancellationToken);

    var latestActionByKey = new Dictionary<string, (string Method, DateTime CreatedAtUtc, string Until)>(StringComparer.OrdinalIgnoreCase);
    foreach (var action in actionPaths)
    {
        var key = ExtractSmartAlertKeyFromPath(action.Path);
        if (string.IsNullOrWhiteSpace(key) || latestActionByKey.ContainsKey(key))
        {
            continue;
        }

        latestActionByKey[key] = (action.Method, action.CreatedAtUtc, ReadAuditQueryValue(action.Path, "until"));
    }

    var visibleAlerts = alerts
        .Where(alert =>
        {
            if (!latestActionByKey.TryGetValue(alert.Key, out var state))
            {
                return true;
            }

            if (state.Method == "SMART_ALERT_ACK")
            {
                return false;
            }

            if (state.Method == "SMART_ALERT_SNOOZE")
            {
                if (DateOnly.TryParse(state.Until, out var untilDate))
                {
                    return untilDate < today;
                }
                return true;
            }

            return true;
        })
        .OrderByDescending(x => ResolveSmartAlertSeverityWeight(x.Severity))
        .ThenBy(x => x.DaysLeft ?? int.MaxValue)
        .ToList();

    return Results.Ok(new
    {
        daysAhead = safeDaysAhead,
        total = visibleAlerts.Count,
        items = visibleAlerts
    });
});

api.MapGet("/smart-alerts/{key}/explain-risk", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    string key,
    string? language,
    IApplicationDbContext dbContext,
    IComplianceAiService complianceAiService,
    IDateTimeProvider dateTimeProvider,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.BadRequest(new { error = "Alert key is required." });
    }

    var normalizedKey = key.Trim();
    var normalizedLanguage = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();
    var isArabic = normalizedLanguage.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

    if (normalizedKey.StartsWith("DOC:", StringComparison.OrdinalIgnoreCase))
    {
        var rawId = normalizedKey["DOC:".Length..].Trim();
        if (TryParseSmartAlertGuid(rawId, out var alertId))
        {
            var alert = await dbContext.ComplianceAlerts.FirstOrDefaultAsync(x => x.Id == alertId, cancellationToken);
            if (alert is not null)
            {
                var score = await BuildComplianceScoreAsync(dbContext, cancellationToken);
                var aiInput = new ComplianceAiInput(
                    Language: normalizedLanguage,
                    Score: score.Score,
                    Grade: score.Grade,
                    SaudizationPercent: score.SaudizationPercent,
                    WpsCompanyReady: score.WpsCompanyReady,
                    EmployeesMissingPaymentData: score.EmployeesMissingPaymentData,
                    CriticalAlerts: score.CriticalAlerts,
                    WarningAlerts: score.WarningAlerts,
                    NoticeAlerts: score.NoticeAlerts,
                    Recommendations: score.Recommendations,
                    UserPrompt: BuildComplianceAlertRiskPrompt(alert, isArabic));

                var aiResult = await complianceAiService.GenerateBriefAsync(aiInput, cancellationToken);
                var targetWindowDays = alert.DaysLeft <= 7 ? 7 : alert.DaysLeft <= 30 ? 30 : 60;

                return Results.Ok(new
                {
                    key = normalizedKey,
                    provider = aiResult.Provider,
                    usedFallback = aiResult.UsedFallback,
                    explanation = aiResult.Text,
                    nextAction = BuildComplianceAlertNextAction(alert, isArabic),
                    targetWindowDays,
                    generatedAtUtc = dateTimeProvider.UtcNow
                });
            }
        }
    }

    var response = BuildSmartAlertRuleExplanation(normalizedKey, isArabic);
    return Results.Ok(new
    {
        key = normalizedKey,
        provider = "rule-engine",
        usedFallback = true,
        explanation = response.Explanation,
        nextAction = response.NextAction,
        targetWindowDays = response.TargetWindowDays,
        generatedAtUtc = dateTimeProvider.UtcNow
    });
});

api.MapPost("/smart-alerts/{key}/acknowledge", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    string key,
    SmartAlertAcknowledgeRequest request,
    HttpContext httpContext,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.BadRequest(new { error = "Alert key is required." });
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var encodedKey = Uri.EscapeDataString(key.Trim());
    var note = string.IsNullOrWhiteSpace(request.Note) ? "" : request.Note.Trim();
    var encodedNote = Uri.EscapeDataString(note);

    dbContext.AddEntity(new AuditLog
    {
        UserId = userId,
        Method = "SMART_ALERT_ACK",
        Path = $"/api/smart-alerts/{encodedKey}/acknowledge?note={encodedNote}",
        StatusCode = StatusCodes.Status200OK,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        DurationMs = 0
    });

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { key, status = "Acknowledged" });
})
    .AddEndpointFilter<ValidationFilter<SmartAlertAcknowledgeRequest>>();

api.MapPost("/smart-alerts/{key}/snooze", [Authorize(Roles = RoleNames.Owner + "," + RoleNames.Admin + "," + RoleNames.Hr + "," + RoleNames.Manager)] async (
    string key,
    SmartAlertSnoozeRequest request,
    HttpContext httpContext,
    IDateTimeProvider dateTimeProvider,
    IApplicationDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.BadRequest(new { error = "Alert key is required." });
    }

    Guid? userId = null;
    var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (Guid.TryParse(userIdClaim, out var parsedUserId))
    {
        userId = parsedUserId;
    }

    var until = DateOnly.FromDateTime(dateTimeProvider.UtcNow.Date).AddDays(request.Days);
    var encodedKey = Uri.EscapeDataString(key.Trim());
    var note = string.IsNullOrWhiteSpace(request.Note) ? "" : request.Note.Trim();
    var encodedNote = Uri.EscapeDataString(note);

    dbContext.AddEntity(new AuditLog
    {
        UserId = userId,
        Method = "SMART_ALERT_SNOOZE",
        Path = $"/api/smart-alerts/{encodedKey}/snooze?until={until:yyyy-MM-dd}&days={request.Days}&note={encodedNote}",
        StatusCode = StatusCodes.Status200OK,
        IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
        DurationMs = 0
    });

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { key, status = "Snoozed", until = until.ToString("yyyy-MM-dd") });
})
    .AddEndpointFilter<ValidationFilter<SmartAlertSnoozeRequest>>();

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

static async Task<List<PayrollPreApprovalFindingResponse>> BuildPayrollPreApprovalFindingsAsync(
    IApplicationDbContext dbContext,
    PayrollRun run,
    CancellationToken cancellationToken)
{
    var period = await dbContext.PayrollPeriods.FirstOrDefaultAsync(x => x.Id == run.PayrollPeriodId, cancellationToken);
    if (period is null)
    {
        return new List<PayrollPreApprovalFindingResponse>();
    }

    var findings = new List<PayrollPreApprovalFindingResponse>();

    var currentLines = await (from line in dbContext.PayrollLines
                              join employee in dbContext.Employees on line.EmployeeId equals employee.Id
                              where line.PayrollRunId == run.Id
                              select new
                              {
                                  line.EmployeeId,
                                  EmployeeName = employee.FirstName + " " + employee.LastName,
                                  line.BaseSalary,
                                  line.Allowances,
                                  line.Deductions,
                                  line.OvertimeHours,
                                  line.OvertimeAmount,
                                  line.NetAmount
                              }).ToListAsync(cancellationToken);

    var previousMonthDate = new DateTime(period.Year, period.Month, 1).AddMonths(-1);
    var previousPeriod = await dbContext.PayrollPeriods
        .Where(x => x.Year == previousMonthDate.Year && x.Month == previousMonthDate.Month)
        .OrderByDescending(x => x.CreatedAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    Dictionary<Guid, (decimal NetAmount, decimal OvertimeHours)> previousByEmployeeId = new();
    if (previousPeriod is not null)
    {
        var previousRun = await dbContext.PayrollRuns
            .Where(x =>
                x.PayrollPeriodId == previousPeriod.Id &&
                (x.Status == PayrollRunStatus.Approved || x.Status == PayrollRunStatus.Locked))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (previousRun is not null)
        {
            var previousLines = await dbContext.PayrollLines
                .Where(x => x.PayrollRunId == previousRun.Id)
                .Select(x => new { x.EmployeeId, x.NetAmount, x.OvertimeHours })
                .ToListAsync(cancellationToken);

            previousByEmployeeId = previousLines.ToDictionary(
                x => x.EmployeeId,
                x => (x.NetAmount, x.OvertimeHours));
        }
    }

    foreach (var line in currentLines)
    {
        if (line.NetAmount < 0)
        {
            findings.Add(new PayrollPreApprovalFindingResponse(
                "NegativeNetAmount",
                "Critical",
                line.EmployeeId,
                line.EmployeeName,
                "Net salary is negative. Review deductions and allowances.",
                "NetAmount",
                line.NetAmount));
        }

        var gross = line.BaseSalary + line.Allowances + line.OvertimeAmount;
        if (gross > 0)
        {
            var deductionPercent = Math.Round((line.Deductions / gross) * 100m, 2);
            if (deductionPercent >= 60m)
            {
                findings.Add(new PayrollPreApprovalFindingResponse(
                    "VeryHighDeductionRatio",
                    "Critical",
                    line.EmployeeId,
                    line.EmployeeName,
                    $"Deductions are {deductionPercent:F2}% of gross salary.",
                    "DeductionPercent",
                    deductionPercent));
            }
            else if (deductionPercent >= 35m)
            {
                findings.Add(new PayrollPreApprovalFindingResponse(
                    "HighDeductionRatio",
                    "Warning",
                    line.EmployeeId,
                    line.EmployeeName,
                    $"Deductions are {deductionPercent:F2}% of gross salary.",
                    "DeductionPercent",
                    deductionPercent));
            }
        }

        if (line.OvertimeHours >= 60m)
        {
            findings.Add(new PayrollPreApprovalFindingResponse(
                "ExtremeOvertime",
                "Critical",
                line.EmployeeId,
                line.EmployeeName,
                $"Overtime hours reached {line.OvertimeHours:F2} this period.",
                "OvertimeHours",
                line.OvertimeHours));
        }
        else if (line.OvertimeHours >= 35m)
        {
            findings.Add(new PayrollPreApprovalFindingResponse(
                "HighOvertime",
                "Warning",
                line.EmployeeId,
                line.EmployeeName,
                $"Overtime hours reached {line.OvertimeHours:F2} this period.",
                "OvertimeHours",
                line.OvertimeHours));
        }

        if (previousByEmployeeId.TryGetValue(line.EmployeeId, out var previous))
        {
            if (previous.NetAmount > 0)
            {
                var deviationPercent = Math.Round(((line.NetAmount - previous.NetAmount) / previous.NetAmount) * 100m, 2);
                var absDeviation = Math.Abs(deviationPercent);

                if (absDeviation >= 35m)
                {
                    findings.Add(new PayrollPreApprovalFindingResponse(
                        "NetDeviationCritical",
                        "Critical",
                        line.EmployeeId,
                        line.EmployeeName,
                        $"Net salary deviated by {deviationPercent:F2}% compared to previous month.",
                        "NetDeviationPercent",
                        deviationPercent));
                }
                else if (absDeviation >= 20m)
                {
                    findings.Add(new PayrollPreApprovalFindingResponse(
                        "NetDeviationWarning",
                        "Warning",
                        line.EmployeeId,
                        line.EmployeeName,
                        $"Net salary deviated by {deviationPercent:F2}% compared to previous month.",
                        "NetDeviationPercent",
                        deviationPercent));
                }
            }

            if (previous.OvertimeHours > 0)
            {
                var overtimeSpikePercent = Math.Round(((line.OvertimeHours - previous.OvertimeHours) / previous.OvertimeHours) * 100m, 2);
                if (overtimeSpikePercent >= 100m && line.OvertimeHours - previous.OvertimeHours >= 8m)
                {
                    findings.Add(new PayrollPreApprovalFindingResponse(
                        "OvertimeSpike",
                        "Warning",
                        line.EmployeeId,
                        line.EmployeeName,
                        $"Overtime increased by {overtimeSpikePercent:F2}% compared to previous month.",
                        "OvertimeSpikePercent",
                        overtimeSpikePercent));
                }
            }
            else if (line.OvertimeHours >= 20m)
            {
                findings.Add(new PayrollPreApprovalFindingResponse(
                    "NewHighOvertime",
                    "Warning",
                    line.EmployeeId,
                    line.EmployeeName,
                    $"Overtime is {line.OvertimeHours:F2} hours while previous month had no overtime.",
                    "OvertimeHours",
                    line.OvertimeHours));
            }
        }
    }

    return findings;
}

static string ReadAuditQueryValue(string path, string key)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return string.Empty;
    }

    var questionMarkIndex = path.IndexOf('?');
    if (questionMarkIndex < 0 || questionMarkIndex + 1 >= path.Length)
    {
        return string.Empty;
    }

    var query = path[(questionMarkIndex + 1)..];
    var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    foreach (var part in parts)
    {
        var keyValue = part.Split('=', 2);
        if (keyValue.Length != 2)
        {
            continue;
        }

        if (string.Equals(keyValue[0], key, StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UnescapeDataString(keyValue[1]);
        }
    }

    return string.Empty;
}

static Guid? TryReadRunIdFromAuditPath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return null;
    }

    var marker = "/api/payroll/runs/";
    var start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (start < 0)
    {
        return null;
    }

    start += marker.Length;
    if (start >= path.Length)
    {
        return null;
    }

    var endSlash = path.IndexOf('/', start);
    var endQuestion = path.IndexOf('?', start);

    var end = endSlash < 0 ? path.Length : endSlash;
    if (endQuestion >= 0 && endQuestion < end)
    {
        end = endQuestion;
    }

    if (end <= start)
    {
        return null;
    }

    var runIdSegment = path[start..end];
    return Guid.TryParse(runIdSegment, out var runId) ? runId : null;
}

static bool TryParseSmartAlertGuid(string raw, out Guid id)
{
    if (Guid.TryParse(raw, out id))
    {
        return true;
    }

    return Guid.TryParseExact(raw, "N", out id);
}

static string ExtractSmartAlertKeyFromPath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return string.Empty;
    }

    var marker = "/api/smart-alerts/";
    var start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (start < 0)
    {
        return string.Empty;
    }

    start += marker.Length;
    if (start >= path.Length)
    {
        return string.Empty;
    }

    var endSlash = path.IndexOf('/', start);
    var endQuestion = path.IndexOf('?', start);
    var end = endSlash < 0 ? path.Length : endSlash;
    if (endQuestion >= 0 && endQuestion < end)
    {
        end = endQuestion;
    }

    if (end <= start)
    {
        return string.Empty;
    }

    var encodedKey = path[start..end];
    return Uri.UnescapeDataString(encodedKey);
}

static (string Explanation, string NextAction, int TargetWindowDays) BuildSmartAlertRuleExplanation(string key, bool isArabic)
{
    var normalized = key.Trim();
    var prefix = normalized.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? normalized;

    if (isArabic)
    {
        return prefix.ToUpperInvariant() switch
        {
            "PROBATION" => (
                "نهاية فترة التجربة قريبة، والتأخر في القرار قد يسبب ارتباكًا تشغيليًا ومخاطر امتثال داخلية.",
                "حدد قرار التثبيت أو الإنهاء الآن، ووثق النتيجة في ملف الموظف قبل تاريخ نهاية التجربة.",
                7),
            "CONTRACT_END" => (
                "انتهاء العقد بدون قرار مسبق قد يسبب انقطاعًا تشغيليًا أو تعثرًا في الإجراءات النظامية.",
                "ابدأ مراجعة التجديد أو الإنهاء النظامي فورًا، وحدد القرار النهائي مبكرًا.",
                30),
            "MISSING_IBAN" => (
                "غياب الآيبان يمنع صرف الرواتب بسلاسة ويرفع احتمال أخطاء أو تأخير WPS.",
                "استكمل آيبان الموظف المعتمد من البنك قبل دورة الرواتب القادمة.",
                7),
            "MISSING_GOSI_SETUP" => (
                "الموظف مؤهل للتأمينات لكن إعداد الأجر الأساسي للتأمينات غير مكتمل، ما قد يسبب أخطاء استقطاع.",
                "حدث أجر التأمينات الأساسي فورًا ثم أعد التحقق قبل اعتماد الرواتب.",
                7),
            "MISSING_CONTRACT_END" => (
                "غياب تاريخ نهاية العقد للموظف غير السعودي يقلل القدرة على التنبؤ بمخاطر التجديد والانتهاء.",
                "أضف تاريخ نهاية العقد في ملف الموظف لتفعيل تنبيهات الامتثال بشكل صحيح.",
                30),
            "PAYROLL_PENDING" => (
                "عدم اعتماد الرواتب قبل موعد الصرف يرفع مخاطر تأخير الرواتب وشكاوى الموظفين.",
                "أكمل فحوصات ما قبل الاعتماد واعتمد الرواتب قبل يوم الصرف الافتراضي.",
                7),
            "OVERTIME_PENDING" => (
                "ساعات العمل الإضافي مرتفعة وتحتاج مراجعة قبل اعتماد الرواتب لتفادي تضخم تكلفة الرواتب أو أخطاء احتساب.",
                "راجع مبررات وساعات العمل الإضافي واعتمد الساعات الصحيحة قبل اعتماد الرواتب.",
                7),
            _ => (
                "هذا التنبيه يشير إلى خطر تشغيلي أو امتثال يحتاج إجراءً موثقًا.",
                "راجع السبب الجذري ونفذ الإجراء التصحيحي وأغلق التنبيه بعد التوثيق.",
                30)
        };
    }

    return prefix.ToUpperInvariant() switch
    {
        "PROBATION" => (
            "Probation is ending soon; delayed decisions can create operational and HR compliance risk.",
            "Finalize confirmation or compliant offboarding now and document the decision before probation end date.",
            7),
        "CONTRACT_END" => (
            "Contract end is approaching; late decisions can cause service disruption or compliance issues.",
            "Start renewal or compliant offboarding workflow now and lock decision early.",
            30),
        "MISSING_IBAN" => (
            "Missing IBAN can block salary disbursement and increase WPS delay risk.",
            "Complete and verify employee IBAN before the next payroll cycle.",
            7),
        "MISSING_GOSI_SETUP" => (
            "GOSI-eligible employee has incomplete GOSI wage setup, which can cause payroll contribution errors.",
            "Update GOSI basic wage immediately and re-run validation before payroll approval.",
            7),
        "MISSING_CONTRACT_END" => (
            "Missing non-Saudi contract end date reduces control over renewal and expiry compliance.",
            "Add contract end date in employee profile to enable accurate compliance tracking.",
            30),
        "PAYROLL_PENDING" => (
            "Payroll approval is still pending close to pay day, increasing salary delay and employee trust risk.",
            "Complete pre-approval checks and approve payroll before default pay day.",
            7),
        "OVERTIME_PENDING" => (
            "Overtime input is high and needs review before payroll approval to avoid cost spikes or calculation errors.",
            "Review overtime justification and hours, then approve corrected values before payroll approval.",
            7),
        _ => (
            "This alert indicates an operational or compliance risk requiring documented action.",
            "Review the root cause, apply corrective action, and close the alert with evidence.",
            30)
    };
}

static int ResolveSmartAlertSeverityWeight(string severity)
{
    return severity switch
    {
        "Critical" => 3,
        "Warning" => 2,
        _ => 1
    };
}

static bool TryParseOverrideReferenceId(string referenceId, out string monthKey, out int sequence)
{
    monthKey = string.Empty;
    sequence = 0;

    if (string.IsNullOrWhiteSpace(referenceId))
    {
        return false;
    }

    var parts = referenceId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length != 3)
    {
        return false;
    }

    if (!string.Equals(parts[0], "OVR", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (parts[1].Length != 6 || !parts[1].All(char.IsDigit))
    {
        return false;
    }

    if (parts[2].Length != 4 || !int.TryParse(parts[2], out sequence) || sequence <= 0)
    {
        return false;
    }

    monthKey = parts[1];
    return true;
}

static string BuildNextOverrideReferenceId(string monthKey, IEnumerable<string> auditPaths)
{
    var maxSequence = 0;
    foreach (var path in auditPaths)
    {
        var referenceId = ReadAuditQueryValue(path, "referenceId");
        if (!TryParseOverrideReferenceId(referenceId, out var refMonthKey, out var sequence))
        {
            continue;
        }

        if (!string.Equals(refMonthKey, monthKey, StringComparison.Ordinal))
        {
            continue;
        }

        maxSequence = Math.Max(maxSequence, sequence);
    }

    var nextSequence = Math.Min(maxSequence + 1, 9999);
    return $"OVR-{monthKey}-{nextSequence:D4}";
}

static string EncodeApprovalFindingsSnapshot(IEnumerable<PayrollPreApprovalFindingResponse> findings)
{
    var snapshot = findings
        .Take(200)
        .Select(x => new
        {
            x.Code,
            x.Severity,
            x.EmployeeId,
            x.EmployeeName,
            x.Message,
            x.MetricName,
            x.MetricValue
        })
        .ToList();

    var json = JsonSerializer.Serialize(snapshot);
    return Uri.EscapeDataString(json);
}

static string ComputeSaudizationRiskLevel(decimal percent, decimal targetPercent)
{
    if (percent >= targetPercent)
    {
        return "Low";
    }

    if (percent >= targetPercent - 5m)
    {
        return "Medium";
    }

    return "High";
}

static string ComputeSaudizationBand(decimal percent, decimal targetPercent)
{
    if (percent >= targetPercent)
    {
        return "Green";
    }

    if (percent >= targetPercent - 5m)
    {
        return "Yellow";
    }

    return "Red";
}

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
    var saudizationTargetPercent = company is null
        ? 30m
        : Math.Clamp(company.NitaqatTargetPercent, 0m, 100m);
    var saudizationGapPercent = Math.Max(0m, saudizationTargetPercent - saudizationPercent);
    var requiredSaudiEmployees = totalEmployees == 0
        ? 1
        : (int)Math.Ceiling((saudizationTargetPercent / 100m) * totalEmployees);
    var additionalSaudiEmployeesNeeded = Math.Max(0, requiredSaudiEmployees - saudiEmployees);

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

    if (saudizationPercent < saudizationTargetPercent)
    {
        score -= Math.Min(20m, (saudizationTargetPercent - saudizationPercent) * 1.2m);
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

    if (saudizationPercent < saudizationTargetPercent)
    {
        recommendations.Add($"Raise Saudization ratio to at least {saudizationTargetPercent:F0}% target (need {additionalSaudiEmployeesNeeded} additional Saudi employee(s) at current headcount).");
    }

    if (recommendations.Count == 0)
    {
        recommendations.Add("Maintain current controls and run weekly compliance review.");
    }

    return new ComplianceScoreResponse(
        finalScore,
        grade,
        saudizationPercent,
        saudizationTargetPercent,
        saudizationGapPercent,
        saudiEmployees,
        totalEmployees,
        additionalSaudiEmployeesNeeded,
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

static string BuildComplianceAlertRiskPrompt(ComplianceAlert alert, bool isArabic)
{
    var timelineDays = alert.DaysLeft <= 7 ? 7 : alert.DaysLeft <= 30 ? 30 : 60;
    if (isArabic)
    {
        return $"اشرح مخاطر تنبيه امتثال واحد بشكل عملي: الموظف {alert.EmployeeName}، نوع المستند {alert.DocumentType}، الشدة {alert.Severity}، متبقي {alert.DaysLeft} يوم، تاريخ الانتهاء {alert.ExpiryDate:yyyy-MM-dd}. المطلوب: 3-5 نقاط قصيرة تتضمن الخطر، الأثر، الإجراء الفوري، وخطة إغلاق خلال {timelineDays} يوم.";
    }

    return $"Explain one compliance alert with practical detail: employee {alert.EmployeeName}, document type {alert.DocumentType}, severity {alert.Severity}, {alert.DaysLeft} day(s) left, expiry {alert.ExpiryDate:yyyy-MM-dd}. Return 3-5 short bullets covering risk, business impact, immediate action, and closure plan within {timelineDays} days.";
}

static string BuildComplianceAlertNextAction(ComplianceAlert alert, bool isArabic)
{
    var timelineDays = alert.DaysLeft <= 7 ? 7 : alert.DaysLeft <= 30 ? 30 : 60;
    if (isArabic)
    {
        return alert.DocumentType switch
        {
            "Iqama" => $"ابدأ تجديد الإقامة فورًا، أكد رفع الطلب خلال 24 ساعة، وأغلق الخطر خلال {timelineDays} يوم.",
            "WorkPermit" => $"ابدأ تجديد رخصة العمل فورًا، حدث حالة السداد/الإصدار، وأغلق الخطر خلال {timelineDays} يوم.",
            "Contract" => $"راجع العقد مع الموظف الآن، وجهز التجديد أو الإنهاء النظامي مبكرًا، ووثق القرار خلال {timelineDays} يوم.",
            _ => $"ابدأ معالجة هذا الخطر فورًا وأغلق التنبيه بعد توثيق الإجراء خلال {timelineDays} يوم."
        };
    }

    return alert.DocumentType switch
    {
        "Iqama" => $"Start Iqama renewal immediately, confirm submission within 24 hours, and close this risk within {timelineDays} days.",
        "WorkPermit" => $"Start work permit renewal immediately, update payment or issuance status, and close this risk within {timelineDays} days.",
        "Contract" => $"Review the employment contract now, prepare renewal or compliant offboarding early, and document the decision within {timelineDays} days.",
        _ => $"Start remediation for this document risk immediately and close the alert with documented action within {timelineDays} days."
    };
}

static async Task<EmailSendResult> TrySendComplianceDigestEmailAsync(
    IConfiguration configuration,
    string toEmail,
    string subject,
    string textBody,
    string htmlBody,
    CancellationToken cancellationToken)
{
    static string? Cfg(IConfiguration cfg, string key)
    {
        var sectionKey = $"Smtp:{key}";
        var doubleUnderscoreKey = $"Smtp__{key}";
        var underscoreKey = $"Smtp_{key}";
        return cfg[sectionKey] ?? cfg[doubleUnderscoreKey] ?? cfg[underscoreKey];
    }

    var host = Cfg(configuration, "Host");
    var port = int.TryParse(Cfg(configuration, "Port"), out var parsedPort) ? parsedPort : 587;
    var username = Cfg(configuration, "Username");
    var password = Cfg(configuration, "Password");
    var fromEmail = Cfg(configuration, "FromEmail") ?? Cfg(configuration, "From");
    var fromName = Cfg(configuration, "FromName") ?? "HR Payroll Compliance";
    var enableSsl = !string.Equals(Cfg(configuration, "EnableSsl"), "false", StringComparison.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromEmail))
    {
        // Keep manual-send usable in dev before SMTP setup.
        return new EmailSendResult(true, true, null);
    }

    try
    {
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

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(mail, cancellationToken);
        return new EmailSendResult(true, false, null);
    }
    catch (Exception ex)
    {
        return new EmailSendResult(false, false, ex.Message);
    }
}

static string BuildLeaveAttachmentMetadata(Guid leaveRequestId) =>
    JsonSerializer.Serialize(new { leaveRequestId });

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

public sealed record PayrollPreApprovalFindingResponse(
    string Code,
    string Severity,
    Guid? EmployeeId,
    string? EmployeeName,
    string Message,
    string? MetricName,
    decimal? MetricValue);

public sealed record LoginRequest(Guid? TenantId, string? TenantSlug, string Email, string Password);
public sealed record ForgotPasswordRequest(string TenantSlug, string Email);
public sealed record ResetPasswordRequest(string TenantSlug, string Email, string Token, string NewPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

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
    int ComplianceDigestHourUtc,
    string NitaqatActivity,
    string NitaqatSizeBand,
    decimal NitaqatTargetPercent);

public sealed record CreateUserRequest(string FirstName, string LastName, string Email, string Password, string Role);
public sealed record AdminResetUserPasswordRequest(string NewPassword);

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
    DateOnly? WorkPermitExpiryDate,
    DateOnly? ContractEndDate);

public sealed record UpdateEmployeeRequest(
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
    DateOnly? WorkPermitExpiryDate,
    DateOnly? ContractEndDate);

public sealed record CreateEmployeeLoginRequest(string Password);

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
    decimal SaudizationTargetPercent,
    decimal SaudizationGapPercent,
    int SaudiEmployees,
    int TotalEmployees,
    int AdditionalSaudiEmployeesNeeded,
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

public static class OffboardingWorkflowGuards
{
    public static async Task<string?> GetFinalSettlementBlockerAsync(
        Guid employeeId,
        IApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var activeOffboarding = await dbContext.EmployeeOffboardings
            .Where(x => x.EmployeeId == employeeId && x.Status != "Closed")
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeOffboarding is null)
        {
            return null;
        }

        if (activeOffboarding.Status != "Approved")
        {
            return "Final settlement is blocked until offboarding request is approved.";
        }

        var checklist = await dbContext.OffboardingChecklists
            .FirstOrDefaultAsync(x => x.OffboardingId == activeOffboarding.Id, cancellationToken);

        if (checklist is null)
        {
            return "Final settlement is blocked because offboarding checklist is missing.";
        }

        var items = await dbContext.OffboardingChecklistItems
            .Where(x => x.ChecklistId == checklist.Id)
            .Select(x => new { x.Status, x.Notes })
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return "Final settlement is blocked because no offboarding checklist items are defined.";
        }

        var requiredItems = items.Where(x => !x.Notes.Contains("Required:False", StringComparison.OrdinalIgnoreCase)).ToList();
        var incompleteRequiredCount = requiredItems.Count(x =>
            x.Status != "Completed" &&
            x.Status != "Approved" &&
            x.Status != "Signed");

        if (incompleteRequiredCount > 0)
        {
            return $"Final settlement is blocked until {incompleteRequiredCount} required offboarding checklist item(s) are completed.";
        }

        return null;
    }
}

public sealed record LeaveBalancePreviewRequest(
    Guid? EmployeeId,
    LeaveType LeaveType,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record ApproveLeaveRequestRequest(string? Comment);

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
public sealed record CreateEmployeeLoanRequest(
    Guid EmployeeId,
    string LoanType,
    decimal PrincipalAmount,
    decimal InstallmentAmount,
    int StartYear,
    int StartMonth,
    int TotalInstallments,
    string? Notes);
public sealed record RescheduleEmployeeLoanRequest(
    int StartYear,
    int StartMonth,
    string? Reason);
public sealed record SkipEmployeeLoanInstallmentRequest(string? Reason);
public sealed record SettleEmployeeLoanRequest(decimal? Amount, string? Reason);
public sealed record CreateOffboardingChecklistTemplateRequest(
    string RoleName,
    string ItemCode,
    string ItemLabel,
    int SortOrder,
    bool IsRequired,
    bool RequiresApproval,
    bool RequiresEsign,
    bool IsActive);
public sealed record ApplyOffboardingChecklistTemplateRequest(string RoleName, bool ReplaceExisting);
public sealed record OffboardingChecklistApprovalRequest(string? Notes);
public sealed record OffboardingChecklistEsignRequest(string DocumentName, string DocumentUrl);
public sealed record CreateShiftRuleRequest(
    string Name,
    decimal StandardDailyHours,
    decimal OvertimeMultiplierWeekday,
    decimal OvertimeMultiplierWeekend,
    decimal OvertimeMultiplierHoliday,
    string WeekendDaysCsv,
    bool IsActive);
public sealed record UpsertTimesheetEntryRequest(
    Guid EmployeeId,
    Guid? ShiftRuleId,
    DateOnly WorkDate,
    decimal HoursWorked,
    bool IsHoliday,
    string? Notes);
public sealed record ApproveTimesheetEntryRequest(
    bool Approved,
    decimal? ApprovedOvertimeHours,
    string? Notes);
public sealed record UpsertAllowancePolicyRequest(
    string PolicyName,
    string JobTitle,
    decimal MonthlyAmount,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsTaxable,
    bool IsActive);
public sealed record CreateEmployeeOffboardingRequest(
    Guid EmployeeId,
    DateOnly EffectiveDate,
    string Reason,
    string? ChecklistRoleName);
public sealed record CreateOffboardingChecklistItemRequest(
    string ItemCode,
    string ItemLabel,
    int SortOrder,
    string? Notes);
public sealed record CompleteOffboardingChecklistItemRequest(string? Notes);
public sealed record ApprovePayrollRunOverrideRequest(string Category, string Reason, string ReferenceId);
public sealed record SmartAlertAcknowledgeRequest(string? Note);
public sealed record SmartAlertSnoozeRequest(int Days, string? Note);
public sealed record SmartAlertResponse(
    string Key,
    string Type,
    string Severity,
    string? EmployeeName,
    string Message,
    int? DaysLeft,
    string? DueDate);

