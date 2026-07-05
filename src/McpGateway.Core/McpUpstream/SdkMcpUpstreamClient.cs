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
    private readonly Func<string, CancellationToken, Task<IMcpClientSession>> _sessionFactory;

    public SdkMcpUpstreamClient(ILoggerFactory? loggerFactory = null)
    {
        var lf = loggerFactory ?? NullLoggerFactory.Instance;
        _sessionFactory = (endpoint, ct) => CreateRealSessionAsync(endpoint, lf, ct);
    }

    internal SdkMcpUpstreamClient(Func<string, CancellationToken, Task<IMcpClientSession>> sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public async Task<IReadOnlyList<UpstreamTool>> ListToolsAsync(string endpoint, CancellationToken ct = default)
    {
        await using var session = await _sessionFactory(endpoint, ct).ConfigureAwait(false);
        var tools = await session.ListToolsAsync(ct).ConfigureAwait(false);

        return tools.Select(t => new UpstreamTool(
            t.Name,
            t.Description,
            ToJsonNode(t.InputSchema))).ToList();
    }

    public async Task<ToolCallResult> CallToolAsync(
        string endpoint,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        await using var session = await _sessionFactory(endpoint, ct).ConfigureAwait(false);
        var result = await session.CallToolAsync(toolName, arguments, ct).ConfigureAwait(false);

        return MapResult(result);
    }

    private static async Task<IMcpClientSession> CreateRealSessionAsync(
        string endpoint,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint),
                TransportMode = HttpTransportMode.StreamableHttp
            },
            loggerFactory);

        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "McpGateway", Version = "1.0.0" }
        };

        var client = await McpClient.CreateAsync(transport, options, loggerFactory, ct).ConfigureAwait(false);
        return new McpClientSession(client);
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

internal interface IMcpClientSession : IAsyncDisposable
{
    Task<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct);

    Task<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct);
}

internal sealed class McpClientSession : IMcpClientSession
{
    private readonly McpClient _client;

    public McpClientSession(McpClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct)
    {
        var tools = await _client.ListToolsAsync(new RequestOptions(), ct).ConfigureAwait(false);
        return tools.Select(t => t.ProtocolTool).ToList();
    }

    public Task<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct)
    {
        return _client.CallToolAsync(
            toolName,
            arguments,
            progress: null,
            new RequestOptions(),
            ct).AsTask();
    }

    public ValueTask DisposeAsync()
    {
        return _client.DisposeAsync();
    }
}
