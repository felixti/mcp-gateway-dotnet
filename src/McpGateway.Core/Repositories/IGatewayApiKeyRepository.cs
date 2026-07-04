using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Repositories;

public interface IGatewayApiKeyRepository
{
    Task<GatewayApiKey?> GetByPrefixAsync(string keyPrefix, CancellationToken ct = default);
    Task<IReadOnlyList<GatewayApiKey>> ListByServerAsync(Guid serverDefinitionId, CancellationToken ct = default);
    Task<GatewayApiKey> AddAsync(GatewayApiKey apiKey, CancellationToken ct = default);
    Task RevokeAsync(Guid id, CancellationToken ct = default);
}
