using McpGateway.Core.Proxy;

namespace McpGateway.Core.McpUpstream;

public interface IMcpUpstreamClient
{
    Task<IReadOnlyList<UpstreamTool>> ListToolsAsync(string endpoint, CancellationToken ct = default);

    Task<ToolCallResult> CallToolAsync(
        string endpoint,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default);
}
