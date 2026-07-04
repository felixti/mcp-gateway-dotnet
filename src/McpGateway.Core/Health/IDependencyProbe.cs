namespace McpGateway.Core.Health;

public interface IDependencyProbe
{
    string Name { get; }
    Task<ProbeResult> ProbeAsync(CancellationToken ct = default);
}

public readonly record struct ProbeResult(bool Ok, string? Error)
{
    public static ProbeResult Success() => new(true, null);
    public static ProbeResult Failure(string error) => new(false, error);
}
