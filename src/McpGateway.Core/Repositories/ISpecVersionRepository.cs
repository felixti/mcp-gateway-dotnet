using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Repositories;

public interface ISpecVersionRepository
{
    Task<SpecVersion> AddAsync(SpecVersion version, CancellationToken ct = default);
    Task<SpecVersion?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SpecVersion>> ListByServerAsync(Guid serverDefinitionId, int limit = 50, CancellationToken ct = default);
    Task<SpecVersion?> GetLatestAsync(Guid serverDefinitionId, CancellationToken ct = default);
}
