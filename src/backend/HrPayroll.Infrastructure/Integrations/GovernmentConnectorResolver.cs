using HrPayroll.Application.Abstractions;

namespace HrPayroll.Infrastructure.Integrations;

public sealed class GovernmentConnectorResolver : IGovernmentConnectorResolver
{
    private readonly IReadOnlyDictionary<string, IGovernmentConnector> _connectorsByProvider;

    public GovernmentConnectorResolver(IEnumerable<IGovernmentConnector> connectors)
    {
        _connectorsByProvider = connectors
            .GroupBy(x => x.Provider, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    public IGovernmentConnector? Resolve(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        _connectorsByProvider.TryGetValue(provider.Trim(), out var connector);
        return connector;
    }
}
