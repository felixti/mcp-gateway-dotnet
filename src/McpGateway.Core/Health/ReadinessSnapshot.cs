namespace McpGateway.Core.Health;

public readonly record struct ReadinessSnapshot(
    bool IsReady,
    bool PostgresOk,
    bool StorageQueueOk,
    bool ToolStoreOk,
    DateTime? LastCheckedAt,
    string? PostgresError,
    string? StorageQueueError,
    string? ToolStoreError);
