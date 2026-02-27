using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HrPayroll.Api.Tests;

public class PayrollApprovalDecisionsTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TestWebApplicationFactory _factory;

    public PayrollApprovalDecisionsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApprovalDecisions_ShouldReturnOk_ForCalculatedRun()
    {
        var client = await CreateAuthenticatedOwnerClientAsync();
        var runId = await CreateCalculatedRunAsync(client);

        var response = await client.GetAsync($"/api/payroll/runs/{runId}/approval-decisions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
    }

    private async Task<HttpClient> CreateAuthenticatedOwnerClientAsync()
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
        return client;
    }

    private async Task<Guid> CreateCalculatedRunAsync(HttpClient client)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var periodResponse = await client.PostAsync("/api/payroll/periods", Json(new
        {
            year = now.Year,
            month = now.Month,
            periodStartDate = new DateOnly(now.Year, now.Month, 1).ToString("yyyy-MM-dd"),
            periodEndDate = new DateOnly(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)).ToString("yyyy-MM-dd")
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
        return runPayload!.RunId;
    }

    private static StringContent Json<T>(T payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions);

    private sealed record CreatedTenantResponse(Guid Id);
    private sealed record LoginResponse(string AccessToken);
    private sealed record PayrollPeriodResponse(Guid Id);
    private sealed record CalculateRunResponse(Guid RunId);
}
