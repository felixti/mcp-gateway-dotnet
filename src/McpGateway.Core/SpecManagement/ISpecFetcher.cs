namespace McpGateway.Core.SpecManagement;

public interface ISpecFetcher
{
    Task<FetchedSpec> FetchAsync(SpecSource source, CancellationToken ct = default);
}
