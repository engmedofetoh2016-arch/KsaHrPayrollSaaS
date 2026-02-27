using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HrPayroll.Api.Tests;

public class PayrollExportValidationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TestWebApplicationFactory _factory;

    public PayrollExportValidationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task WpsExport_ShouldReturnBadRequest_WhenPaymentDataMissing()
    {
        var (client, _) = await CreateAuthenticatedOwnerClientAsync();
        await CreateEmployeeAsync(client, isGosiEligible: true, employeeNumber: "", bankName: "", bankIban: "");
        var runId = await CreateApproveRunAsync(client);

        var response = await client.PostAsync($"/api/payroll/runs/{runId}/exports/wps-csv", EmptyJson());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("WPS export validation failed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WpsExport_ShouldQueue_WhenPaymentDataComplete()
    {
        var (client, _) = await CreateAuthenticatedOwnerClientAsync();
        await ConfigureCompanyWpsAsync(client);
        await CreateEmployeeAsync(
            client,
            isGosiEligible: true,
            employeeNumber: "EMP-1001",
            bankName: "NCB",
            bankIban: "SA4420000001234567891234");
        var runId = await CreateApproveRunAsync(client);

        var response = await client.PostAsync($"/api/payroll/runs/{runId}/exports/wps-csv", EmptyJson());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("id", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GosiExport_ShouldReturnBadRequest_WhenGosiDataMismatched()
    {
        var (client, _) = await CreateAuthenticatedOwnerClientAsync();
        var employee = await CreateEmployeeAsync(
            client,
            isGosiEligible: true,
            employeeNumber: "EMP-2001",
            bankName: "NCB",
            bankIban: "SA4420000001234567891235");
        var runId = await CreateApproveRunAsync(client);

        var updatedEmployee = employee with { GosiBasicWage = employee.GosiBasicWage + 500m };
        var updateResponse = await client.PutAsync(
            $"/api/employees/{employee.Id}",
            Json(updatedEmployee));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var response = await client.PostAsync($"/api/payroll/runs/{runId}/exports/gosi-csv", EmptyJson());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("GOSI export validation failed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GosiExport_ShouldQueue_WhenGosiDataValid()
    {
        var (client, _) = await CreateAuthenticatedOwnerClientAsync();
        await CreateEmployeeAsync(
            client,
            isGosiEligible: true,
            employeeNumber: "EMP-3001",
            bankName: "NCB",
            bankIban: "SA4420000001234567891236");
        var runId = await CreateApproveRunAsync(client);

        var response = await client.PostAsync($"/api/payroll/runs/{runId}/exports/gosi-csv", EmptyJson());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("id", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(HttpClient Client, Guid TenantId)> CreateAuthenticatedOwnerClientAsync()
    {
        var client = _factory.CreateClient();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var slug = $"tenant-{unique}";
        var ownerEmail = $"owner-{unique}@example.com";
        const string ownerPassword = "OwnerPass123";

        var createTenantResponse = await client.PostAsync("/api/tenants", Json(new
        {
            tenantName = $"Tenant {unique}",
            slug,
            companyLegalName = $"Company {unique}",
            currencyCode = "SAR",
            defaultPayDay = 25,
            ownerFirstName = "Owner",
            ownerLastName = "User",
            ownerEmail,
            ownerPassword
        }));
        Assert.Equal(HttpStatusCode.Created, createTenantResponse.StatusCode);
        var tenantPayload = Deserialize<CreatedTenantResponse>(await createTenantResponse.Content.ReadAsStringAsync());
        Assert.NotNull(tenantPayload);

        var loginResponse = await client.PostAsync("/api/auth/login", Json(new
        {
            tenantId = tenantPayload!.Id,
            email = ownerEmail,
            password = ownerPassword
        }));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginPayload = Deserialize<LoginResponse>(await loginResponse.Content.ReadAsStringAsync());
        Assert.NotNull(loginPayload);
        Assert.False(string.IsNullOrWhiteSpace(loginPayload!.AccessToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginPayload.AccessToken);
        client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantPayload.Id.ToString());
        return (client, tenantPayload.Id);
    }

    private async Task ConfigureCompanyWpsAsync(HttpClient client)
    {
        var profileResponse = await client.GetAsync("/api/company-profile");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profile = Deserialize<CompanyProfileResponse>(await profileResponse.Content.ReadAsStringAsync());
        Assert.NotNull(profile);

        var updateResponse = await client.PutAsync("/api/company-profile", Json(new
        {
            legalName = profile!.LegalName,
            currencyCode = profile.CurrencyCode,
            defaultPayDay = profile.DefaultPayDay,
            eosFirstFiveYearsMonthFactor = profile.EosFirstFiveYearsMonthFactor,
            eosAfterFiveYearsMonthFactor = profile.EosAfterFiveYearsMonthFactor,
            wpsCompanyBankName = "NCB",
            wpsCompanyBankCode = "2000",
            wpsCompanyIban = "SA4420000001234567899999",
            complianceDigestEnabled = profile.ComplianceDigestEnabled,
            complianceDigestEmail = profile.ComplianceDigestEmail ?? string.Empty,
            complianceDigestFrequency = string.IsNullOrWhiteSpace(profile.ComplianceDigestFrequency) ? "Weekly" : profile.ComplianceDigestFrequency,
            complianceDigestHourUtc = profile.ComplianceDigestHourUtc,
            nitaqatActivity = profile.NitaqatActivity,
            nitaqatSizeBand = profile.NitaqatSizeBand,
            nitaqatTargetPercent = profile.NitaqatTargetPercent
        }));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
    }

    private async Task<EmployeeResponse> CreateEmployeeAsync(
        HttpClient client,
        bool isGosiEligible,
        string employeeNumber,
        string bankName,
        string bankIban)
    {
        var unique = Guid.NewGuid().ToString("N")[..6];
        var response = await client.PostAsync("/api/employees", Json(new
        {
            startDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).ToString("yyyy-MM-dd"),
            firstName = "Test",
            lastName = $"Employee{unique}",
            email = $"employee-{unique}@example.com",
            jobTitle = "Engineer",
            baseSalary = 8000m,
            isSaudiNational = true,
            isGosiEligible = isGosiEligible,
            gosiBasicWage = isGosiEligible ? 7000m : 0m,
            gosiHousingAllowance = isGosiEligible ? 1000m : 0m,
            employeeNumber,
            bankName,
            bankIban,
            iqamaNumber = "",
            iqamaExpiryDate = (string?)null,
            workPermitExpiryDate = (string?)null,
            contractEndDate = (string?)null,
            gradeCode = "G1",
            locationCode = "RIYADH"
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var employee = Deserialize<EmployeeResponse>(await response.Content.ReadAsStringAsync());
        Assert.NotNull(employee);
        return employee!;
    }

    private async Task<Guid> CreateApproveRunAsync(HttpClient client)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var periodResponse = await client.PostAsync("/api/payroll/periods", Json(new
        {
            year = today.Year,
            month = today.Month,
            periodStartDate = new DateOnly(today.Year, today.Month, 1).ToString("yyyy-MM-dd"),
            periodEndDate = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)).ToString("yyyy-MM-dd")
        }));
        Assert.Equal(HttpStatusCode.Created, periodResponse.StatusCode);
        var period = Deserialize<PayrollPeriodResponse>(await periodResponse.Content.ReadAsStringAsync());
        Assert.NotNull(period);

        var calculateResponse = await client.PostAsync("/api/payroll/runs/calculate", Json(new
        {
            payrollPeriodId = period!.Id
        }));
        Assert.Equal(HttpStatusCode.OK, calculateResponse.StatusCode);
        var runPayload = Deserialize<CalculateRunResponse>(await calculateResponse.Content.ReadAsStringAsync());
        Assert.NotNull(runPayload);

        var approveResponse = await client.PostAsync($"/api/payroll/runs/{runPayload!.RunId}/approve", EmptyJson());
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        return runPayload.RunId;
    }

    private static StringContent Json<T>(T payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static StringContent EmptyJson()
        => new("{}", Encoding.UTF8, "application/json");

    private static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions);

    private sealed record CreatedTenantResponse(Guid Id);

    private sealed record LoginResponse(string AccessToken);

    private sealed record PayrollPeriodResponse(Guid Id);

    private sealed record CalculateRunResponse(Guid RunId);

    private sealed record CompanyProfileResponse(
        string LegalName,
        string CurrencyCode,
        int DefaultPayDay,
        decimal EosFirstFiveYearsMonthFactor,
        decimal EosAfterFiveYearsMonthFactor,
        bool ComplianceDigestEnabled,
        string? ComplianceDigestEmail,
        string? ComplianceDigestFrequency,
        int ComplianceDigestHourUtc,
        string NitaqatActivity,
        string NitaqatSizeBand,
        decimal NitaqatTargetPercent);

    private sealed record EmployeeResponse(
        Guid Id,
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
        DateOnly? ContractEndDate,
        string? GradeCode,
        string? LocationCode);
}
