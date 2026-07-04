namespace McpGateway.Core.Health;

/// <summary>
/// Drains in-memory audit events to Azure Storage Queue. Implemented by the
/// Audit plan; defined here so the graceful shutdown orchestrator can invoke it.
/// </summary>
public interface IAuditFlusher
{
    Task FlushAsync(CancellationToken ct = default);
}
