using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Repositories;

public interface IToolOverrideRepository
{
    Task<ToolOverride?> GetAsync(Guid serverDefinitionId, string toolName, CancellationToken ct = default);
    Task<IReadOnlyList<ToolOverride>> ListByServerAsync(Guid serverDefinitionId, CancellationToken ct = default);
    Task<ToolOverride> UpsertAsync(ToolOverride overrideEntry, CancellationToken ct = default);
    Task DeleteAsync(Guid serverDefinitionId, string toolName, CancellationToken ct = default);
}
