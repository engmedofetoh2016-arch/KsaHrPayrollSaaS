using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HrPayroll.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HrPayroll.Infrastructure.Integrations;

public sealed class QiwaConnector : IGovernmentConnector
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<QiwaOptions> _options;
    private readonly ILogger<QiwaConnector> _logger;

    public QiwaConnector(
        IHttpClientFactory httpClientFactory,
        IOptions<QiwaOptions> options,
        ILogger<QiwaConnector> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public string Provider => "Qiwa";

    public async Task<GovernmentSyncResult> SyncAsync(GovernmentSyncRequest request, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new GovernmentSyncResult(
                false,
                null,
                "{}",
                "Qiwa connector is not configured (enabled/baseUrl/apiKey).");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("QiwaConnector");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));

            using var message = new HttpRequestMessage(HttpMethod.Post, "sync");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            message.Headers.Add("X-Idempotency-Key", request.IdempotencyKey);

            var bodyJson = JsonSerializer.Serialize(new
            {
                provider = request.Provider,
                operation = request.Operation,
                entityType = request.EntityType,
                entityId = request.EntityId,
                payload = request.PayloadJson
            });
            message.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(message, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new GovernmentSyncResult(
                    false,
                    null,
                    string.IsNullOrWhiteSpace(responseJson) ? "{}" : responseJson,
                    $"Qiwa returned {(int)response.StatusCode}.");
            }

            string? externalReference = null;
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                if (doc.RootElement.TryGetProperty("referenceId", out var referenceIdElement) &&
                    referenceIdElement.ValueKind == JsonValueKind.String)
                {
                    externalReference = referenceIdElement.GetString();
                }
            }
            catch
            {
                // keep raw response for traceability
            }

            return new GovernmentSyncResult(
                true,
                externalReference,
                string.IsNullOrWhiteSpace(responseJson) ? "{}" : responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qiwa sync failed for operation {Operation}", request.Operation);
            return new GovernmentSyncResult(false, null, "{}", ex.Message);
        }
    }
}
