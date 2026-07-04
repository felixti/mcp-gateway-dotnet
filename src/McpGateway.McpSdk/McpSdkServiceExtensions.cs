using System.Text.Json;
using System.Text.Json.Nodes;
using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpGateway.McpSdk;

public static class McpSdkServiceExtensions
{
    internal const string ServerNameItemKey = "mcp.server.name";
    internal const int ServerNotApprovedCode = -32005;

    public static IServiceCollection AddMcpGatewayMcp(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "mcp-gateway",
                Version = "0.0.1"
            };
        })
        .WithHttpTransport(opts =>
        {
            opts.Stateless = true;
            opts.ConfigureSessionOptions = (ctx, _, _) =>
            {
                if (ctx.Request.RouteValues.TryGetValue("serverName", out var raw)
                    && raw is string serverName
                    && !string.IsNullOrWhiteSpace(serverName))
                {
                    ctx.Items[ServerNameItemKey] = serverName;
                }
                return Task.CompletedTask;
            };
        })
        .WithListToolsHandler((request, ct) =>
            ValueTask.FromResult(BuildListToolsResult(request.Services)))
        .WithCallToolHandler((request, ct) =>
            BuildCallToolResultAsync(request.Services, request.Params.Name, request.Params.Arguments, ct));

        return services;
    }

    public static IEndpointConventionBuilder MapMcpGateway(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapMcp("/mcp/{serverName}");
    }

    internal static ListToolsResult BuildListToolsResult(IServiceProvider? services)
    {
        if (services is null) return EmptyTools();

        var store = services.GetService(typeof(IToolStore)) as IToolStore;
        var serverName = ResolveServerName(services);
        if (store is null || string.IsNullOrEmpty(serverName)) return EmptyTools();

        var server = store.GetServer(serverName);
        if (server is null || !IsApproved(server)) return EmptyTools();

        var tools = server.Tools
            .Where(t => t.Visible)
            .Select(t => new Tool
            {
                Name = t.ToolName,
                Description = t.Description,
                InputSchema = ParseInputSchema(t.InputSchema),
                OutputSchema = ParseInputSchema(t.OutputSchema),
            })
            .ToList();
        return new ListToolsResult { Tools = tools };
    }

    internal static async ValueTask<CallToolResult> BuildCallToolResultAsync(
        IServiceProvider? services,
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        CancellationToken ct)
    {
        if (services is null)
        {
            return ErrorResult("internal error: missing request services");
        }

        var store = services.GetService(typeof(IToolStore)) as IToolStore;
        var serverName = ResolveServerName(services);
        if (store is null || string.IsNullOrEmpty(serverName))
        {
            return ErrorResult($"[{ServerNotApprovedCode}] server '{serverName}' is not registered");
        }

        var server = store.GetServer(serverName);
        if (server is null || !IsApproved(server))
        {
            return ErrorResult($"[{ServerNotApprovedCode}] server '{serverName}' is not approved");
        }

        var tool = server.Tools.FirstOrDefault(t => t.ToolName == toolName && t.Visible);
        if (tool is null)
        {
            return ErrorResult($"tool '{toolName}' not found on server '{serverName}'");
        }

        var argumentsDict = NormalizeArguments(arguments);

        var proxy = services.GetService(typeof(ToolCallHandler)) as ToolCallHandler;
        if (proxy is null)
        {
            return PlaceholderResult(toolName, argumentsDict);
        }

        return await InvokeProxyAsync(proxy, serverName, toolName, argumentsDict, ct);
    }

    private static async ValueTask<CallToolResult> InvokeProxyAsync(
        ToolCallHandler proxy,
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct)
    {
        var result = await proxy.HandleAsync(serverName, toolName, arguments, ct);
        var content = result.Content
            .Select(c => new TextContentBlock { Text = c.Text })
            .ToList<ContentBlock>();
        return new CallToolResult
        {
            Content = content,
            IsError = result.IsError,
        };
    }

    private static CallToolResult PlaceholderResult(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        var argsJson = arguments.Count > 0 ? JsonSerializer.Serialize(arguments) : "{}";
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"{toolName} ok: {argsJson}" }],
        };
    }

    private static CallToolResult ErrorResult(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = message }],
    };

    private static ListToolsResult EmptyTools() => new() { Tools = [] };

    private static string? ResolveServerName(IServiceProvider services)
    {
        var accessor = services.GetService(typeof(IHttpContextAccessor)) as IHttpContextAccessor;
        var ctx = accessor?.HttpContext;
        if (ctx?.Items.TryGetValue(ServerNameItemKey, out var v) == true && v is string s)
        {
            return s;
        }
        return null;
    }

    private static bool IsApproved(McpServerDefinition server) =>
        string.Equals(server.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase);

    private static JsonElement ParseInputSchema(string? inputSchema)
    {
        var fallback = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        if (string.IsNullOrWhiteSpace(inputSchema)) return fallback;
        try
        {
            var node = JsonNode.Parse(inputSchema);
            return JsonSerializer.SerializeToElement(node ?? JsonNode.Parse("{}"));
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static IReadOnlyDictionary<string, object?> NormalizeArguments(
        IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>(arguments.Count);
        foreach (var (key, value) in arguments)
        {
            result[key] = JsonSerializer.Deserialize<object?>(value.GetRawText());
        }
        return result;
    }
}
