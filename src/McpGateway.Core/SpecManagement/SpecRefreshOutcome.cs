namespace McpGateway.Core.SpecManagement;

public enum SpecRefreshStatus
{
    Unchanged,
    Updated,
    Failed,
    NoSpecSource
}

public sealed record SpecRefreshOutcome(
    SpecRefreshStatus Status,
    string ServerName,
    string? OldHash,
    string? NewHash,
    string? Error = null);
