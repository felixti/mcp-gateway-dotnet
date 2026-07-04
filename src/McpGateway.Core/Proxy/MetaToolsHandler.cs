using System.Text.Json;
using McpGateway.Core.Proxy.Exceptions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;

namespace McpGateway.Core.Proxy;

public class MetaToolsHandler
{
    private readonly IToolStore _toolStore;

    public MetaToolsHandler(IToolStore toolStore)
    {
        _toolStore = toolStore;
    }

    public Task<ToolCallResult> HandleAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        var server = _toolStore.GetServer(serverName)
            ?? throw new KeyNotFoundException($"Server '{serverName}' not found.");

        return toolName switch
        {
            "list_api_endpoints" => Task.FromResult(ListEndpoints(server)),
            "get_api_endpoint_schema" => Task.FromResult(GetEndpointSchema(server, arguments)),
            "invoke_api_endpoint" => throw new NotImplementedException("invoke_api_endpoint delegates to ToolCallHandler."),
            _ => throw new ToolNotFoundException(serverName, toolName)
        };
    }

    private static ToolCallResult ListEndpoints(McpServerDefinition server)
    {
        var endpoints = server.Tools.Select(t => new
        {
            t.ToolName,
            t.Description,
            t.HttpMethod,
            t.HttpPath
        });

        return new ToolCallResult
        {
            Content = [new ToolCallContent { Text = JsonSerializer.Serialize(endpoints) }]
        };
    }

    private static ToolCallResult GetEndpointSchema(McpServerDefinition server, IReadOnlyDictionary<string, object?> arguments)
    {
        var targetName = arguments["tool_name"]?.ToString()
            ?? throw new ArgumentException("tool_name is required.");

        var tool = server.Tools.FirstOrDefault(t => t.ToolName == targetName)
            ?? throw new ToolNotFoundException(server.Name, targetName);

        var schema = new
        {
            tool.ToolName,
            tool.Description,
            tool.HttpMethod,
            tool.HttpPath,
            tool.InputSchema,
            tool.OutputSchema
        };

        return new ToolCallResult
        {
            Content = [new ToolCallContent { Text = JsonSerializer.Serialize(schema) }]
        };
    }
}
