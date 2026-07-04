namespace McpGateway.Core.SpecManagement;

public interface ISpecRefresher
{
    Task<SpecRefreshOutcome> RefreshAsync(string serverName, CancellationToken ct = default);
    Task<IReadOnlyList<SpecRefreshOutcome>> RefreshAllAsync(CancellationToken ct = default);
}
