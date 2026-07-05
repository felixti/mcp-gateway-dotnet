using System.Text.Json;
using System.Text.Json.Nodes;
using McpGateway.Core.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpGateway.Core.McpUpstream;

public sealed class SdkMcpUpstreamClient : IMcpUpstreamClient
{
    private readonly ILoggerFactory _loggerFactory;

    public SdkMcpUpstreamClient(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public async Task<IReadOnlyList<UpstreamTool>> ListToolsAsync(string endpoint, CancellationToken ct = default)
    {
        await using var client = await ConnectAsync(endpoint, ct).ConfigureAwait(false);
        var tools = await client.ListToolsAsync(new RequestOptions(), ct).ConfigureAwait(false);

        return tools.Select(t => new UpstreamTool(
            t.Name,
            t.Description,
            ToJsonNode(t.JsonSchema))).ToList();
    }

    public async Task<ToolCallResult> CallToolAsync(
        string endpoint,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        await using var client = await ConnectAsync(endpoint, ct).ConfigureAwait(false);
        var result = await client.CallToolAsync(
            toolName,
            arguments,
            progress: null,
            new RequestOptions(),
            ct).ConfigureAwait(false);

        return MapResult(result);
    }

    private async Task<McpClient> ConnectAsync(string endpoint, CancellationToken ct)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint),
                TransportMode = HttpTransportMode.StreamableHttp
            },
            _loggerFactory);

        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "McpGateway", Version = "1.0.0" }
        };

        return await McpClient.CreateAsync(transport, options, _loggerFactory, ct).ConfigureAwait(false);
    }

    private static ToolCallResult MapResult(CallToolResult result)
    {
        var contents = new List<ToolCallContent>();

        foreach (var block in result.Content)
        {
            contents.Add(MapContentBlock(block));
        }

        return new ToolCallResult
        {
            IsError = result.IsError ?? false,
            Content = contents
        };
    }

    private static ToolCallContent MapContentBlock(ContentBlock block)
    {
        if (block is TextContentBlock text)
        {
            return new ToolCallContent { Type = "text", Text = text.Text };
        }

        if (block is EmbeddedResourceBlock embedded
            && embedded.Resource is TextResourceContents textResource)
        {
            return new ToolCallContent { Type = "text", Text = textResource.Text };
        }

        return new ToolCallContent
        {
            Type = block.Type,
            Text = JsonSerializer.Serialize(block)
        };
    }

    private static JsonNode? ToJsonNode(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Undefined || schema.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return JsonNode.Parse(schema.GetRawText());
    }
}
