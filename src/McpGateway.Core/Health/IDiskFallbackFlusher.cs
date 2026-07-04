namespace McpGateway.Core.Health;

/// <summary>
/// Pushes buffered audit events from the local disk fallback into Azure Storage Queue.
/// Implemented by the Audit plan; defined here so the graceful shutdown orchestrator
/// can invoke it.
/// </summary>
public interface IDiskFallbackFlusher
{
    Task FlushAsync(CancellationToken ct = default);
}
