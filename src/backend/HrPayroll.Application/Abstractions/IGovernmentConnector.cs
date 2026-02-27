namespace HrPayroll.Application.Abstractions;

public sealed record GovernmentSyncRequest(
    Guid TenantId,
    string Provider,
    string Operation,
    string EntityType,
    Guid? EntityId,
    string PayloadJson,
    string IdempotencyKey);

public sealed record GovernmentSyncResult(
    bool Success,
    string? ExternalReference,
    string ResponseJson,
    string? ErrorMessage = null);

public interface IGovernmentConnector
{
    string Provider { get; }
    Task<GovernmentSyncResult> SyncAsync(GovernmentSyncRequest request, CancellationToken cancellationToken);
}

public interface IGovernmentConnectorResolver
{
    IGovernmentConnector? Resolve(string provider);
}
