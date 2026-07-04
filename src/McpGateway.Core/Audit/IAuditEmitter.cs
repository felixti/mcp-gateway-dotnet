namespace McpGateway.Core.Audit;

public interface IAuditEmitter
{
    Task EmitAsync(AuditEvent auditEvent, CancellationToken ct = default);
}
