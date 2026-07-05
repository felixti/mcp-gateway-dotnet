using McpGateway.Core.McpUpstream;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Proxy;

public sealed class McpUpstreamInvocationStrategy : IToolInvocationStrategy
{
    private readonly IMcpUpstreamClient _client;

    public McpUpstreamInvocationStrategy(IMcpUpstreamClient client)
    {
        _client = client;
    }

    public SourceType SourceType => SourceType.McpUpstream;

    public Task<ToolCallResult> InvokeAsync(
        McpServerDefinition server,
        ToolDefinition tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        return _client.CallToolAsync(server.BaseUrl, tool.ToolName, arguments, ct);
    }
}
