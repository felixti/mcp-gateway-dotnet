namespace McpGateway.Core.Health;

public class HealthOptions
{
    public const string SectionName = "Health";

    /// <summary>
    /// How often the DependencyHealthChecker probes PostgreSQL and Azure Storage Queue.
    /// </summary>
    public int DependencyCheckIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum time the graceful shutdown service waits for in-flight tool calls to drain.
    /// </summary>
    public int ShutdownDrainTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Connection string name used for the PostgreSQL probe.
    /// Resolved at runtime via IConfiguration.GetConnectionString.
    /// </summary>
    public string PostgresConnectionName { get; set; } = "PostgreSql";

    /// <summary>
    /// Connection string name used for the Storage Queue probe.
    /// </summary>
    public string StorageQueueConnectionName { get; set; } = "StorageQueue";
}
