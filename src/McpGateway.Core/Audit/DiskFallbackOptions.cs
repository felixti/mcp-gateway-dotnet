namespace McpGateway.Core.Audit;

public class DiskFallbackOptions
{
    public string Directory { get; set; } = Path.Combine(Path.GetTempPath(), "mcp-gateway-audit-fallback");
    public int MaxBufferBytes { get; set; } = 100 * 1024 * 1024; // 100 MB ceiling
}
