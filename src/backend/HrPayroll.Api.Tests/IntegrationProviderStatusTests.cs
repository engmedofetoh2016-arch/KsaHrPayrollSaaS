using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace HrPayroll.Api.Tests;

public class IntegrationProviderStatusTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TestWebApplicationFactory _factory;

    public IntegrationProviderStatusTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProviderStatus_ShouldReturnQiwaAndMudad_ForAuthorizedUser()
    {
        var client = await CreateAuthenticatedOwnerClientAsync();

        var response = await client.GetAsync("/api/integrations/providers/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("primaryProvider", out var primaryProviderElement));
        Assert.Equal(JsonValueKind.String, primaryProviderElement.ValueKind);

        Assert.True(doc.RootElement.TryGetProperty("providers", out var providersElement));
        Assert.Equal(JsonValueKind.Array, providersElement.ValueKind);
        Assert.Equal(2, providersElement.GetArrayLength());

        var hasQiwa = false;
        var hasMudad = false;
        foreach (var provider in providersElement.EnumerateArray())
        {
            var name = provider.GetProperty("provider").GetString();
            if (string.Equals(name, "Qiwa", StringComparison.OrdinalIgnoreCase))
            {
                hasQiwa = true;
            }

            if (string.Equals(name, "Mudad", StringComparison.OrdinalIgnoreCase))
            {
                hasMudad = true;
            }

            Assert.True(provider.TryGetProperty("isPrimary", out _));
            Assert.True(provider.TryGetProperty("isEnabled", out _));
            Assert.True(provider.TryGetProperty("isReady", out _));
            Assert.True(provider.TryGetProperty("timeoutSeconds", out _));
            Assert.True(provider.TryGetProperty("missingConfig", out _));
            Assert.True(provider.TryGetProperty("stats", out _));
        }

        Assert.True(hasQiwa);
        Assert.True(hasMudad);
    }

    [Fact]
    public async Task ProviderStatus_ShouldRejectAnonymous()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/integrations/providers/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProviderHealth_ShouldReturnBadRequest_ForUnsupportedProvider()
    {
        var client = await CreateAuthenticatedOwnerClientAsync();

        var response = await client.PostAsync("/api/integrations/providers/Unknown/health?dryRun=false", EmptyJson());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unsupported provider", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderHealth_ShouldReturnConfigurationState_WhenDryRunFalse()
    {
        var client = await CreateAuthenticatedOwnerClientAsync();

        var response = await client.PostAsync("/api/integrations/providers/Qiwa/health?dryRun=false", EmptyJson());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Qiwa", doc.RootElement.GetProperty("provider").GetString());
        Assert.False(doc.RootElement.GetProperty("dryRunExecuted").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("missingConfig", out var missingConfig));
        Assert.Equal(JsonValueKind.Array, missingConfig.ValueKind);
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

    private static StringContent Json<T>(T payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static StringContent EmptyJson()
        => new("{}", Encoding.UTF8, "application/json");

    private static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions);

    private sealed record CreatedTenantResponse(Guid Id);

    private sealed record LoginResponse(string AccessToken);
}
