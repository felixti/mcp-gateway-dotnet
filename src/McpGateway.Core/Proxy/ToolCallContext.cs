using McpGateway.Core.Auth;

namespace McpGateway.Core.Proxy;

public class ToolCallContext
{
    public CallerIdentity Caller { get; set; } = null!;
    public string ServerName { get; set; } = null!;
    public string ToolName { get; set; } = null!;
}
