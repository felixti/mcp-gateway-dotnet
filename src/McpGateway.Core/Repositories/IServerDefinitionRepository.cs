using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Repositories;

public interface IServerDefinitionRepository
{
    Task<McpServerDefinition?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<McpServerDefinition?> GetByNameForAdminAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<McpServerDefinition>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<McpServerDefinition>> ListApprovedAsync(CancellationToken ct = default);
    Task<McpServerDefinition> AddAsync(McpServerDefinition definition, CancellationToken ct = default);
    Task UpdateAsync(McpServerDefinition definition, CancellationToken ct = default);
    Task UpdateToolsAsync(Guid serverDefinitionId, IEnumerable<ToolDefinition> tools, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
