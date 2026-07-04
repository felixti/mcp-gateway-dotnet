namespace McpGateway.Core.Audit;

public class QueueEmitterOptions
{
    public const int MaxResponseBytes = 10 * 1024;

    public string ConnectionString { get; set; } = string.Empty;
    public string QueueName { get; set; } = "mcp-audit";
}
