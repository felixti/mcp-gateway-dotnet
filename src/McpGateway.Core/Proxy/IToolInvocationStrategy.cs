using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Proxy;

public interface IToolInvocationStrategy
{
    SourceType SourceType { get; }

    Task<ToolCallResult> InvokeAsync(
        McpServerDefinition server,
        ToolDefinition tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default);
}
