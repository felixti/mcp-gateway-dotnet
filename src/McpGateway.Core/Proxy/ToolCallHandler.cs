using System.Diagnostics;
using McpGateway.Core.Audit;
using McpGateway.Core.Health;
using McpGateway.Core.Proxy.Exceptions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using McpGateway.Telemetry;

namespace McpGateway.Core.Proxy;

public class ToolCallHandler
{
    public const string HttpClientName = "McpToolProxy";

    private readonly IToolStore _toolStore;
    private readonly HttpRequestBuilder _requestBuilder;
    private readonly ResponseWrapper _responseWrapper;
    private readonly HttpClient _httpClient;
    private readonly IAuditEmitter _auditEmitter;
    private readonly TimeProvider _timeProvider;
    private readonly ToolCallContextAccessor _contextAccessor;
    private readonly IInFlightCallTracker _inFlightCallTracker;

    public ToolCallHandler(
        IToolStore toolStore,
        HttpRequestBuilder requestBuilder,
        ResponseWrapper responseWrapper,
        HttpClient httpClient,
        IAuditEmitter auditEmitter,
        TimeProvider timeProvider,
        ToolCallContextAccessor contextAccessor,
        IInFlightCallTracker inFlightCallTracker)
    {
        _toolStore = toolStore;
        _requestBuilder = requestBuilder;
        _responseWrapper = responseWrapper;
        _httpClient = httpClient;
        _auditEmitter = auditEmitter;
        _timeProvider = timeProvider;
        _contextAccessor = contextAccessor;
        _inFlightCallTracker = inFlightCallTracker;
    }

    public async Task<ToolCallResult> HandleAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        using var _inFlight = _inFlightCallTracker.Begin();

        var startedAt = _timeProvider.GetTimestamp();
        var context = _contextAccessor.Current;

        using var activity = ActivitySources.ToolCall.StartActivity("mcp.tool.call", ActivityKind.Internal);
        activity?.SetTag("mcp.server.name", serverName);
        activity?.SetTag("mcp.tool.name", toolName);

        var server = _toolStore.GetServer(serverName)
            ?? throw new KeyNotFoundException($"Server '{serverName}' not found.");

        var tool = server.Tools.FirstOrDefault(t => t.ToolName == toolName)
            ?? throw new ToolNotFoundException(serverName, toolName);

        var argumentsJson = System.Text.Json.JsonSerializer.Serialize(arguments);
        activity?.SetTag("mcp.tool.arguments", argumentsJson);

        var request = _requestBuilder.Build(server.BaseUrl, tool, arguments);
        var response = await _httpClient.SendAsync(request, ct);
        var result = await _responseWrapper.WrapAsync(response);

        var elapsed = _timeProvider.GetElapsedTime(startedAt);
        var latencyMs = (long)elapsed.TotalMilliseconds;

        activity?.SetTag("mcp.tool.http_status", (int)response.StatusCode);
        activity?.SetTag("mcp.tool.is_error", result.IsError);

        TelemetryMetrics.ToolCallCount.Add(1,
            new KeyValuePair<string, object?>("mcp.server.name", serverName),
            new KeyValuePair<string, object?>("mcp.tool.name", toolName));

        TelemetryMetrics.ToolCallLatencyMs.Record(latencyMs,
            new KeyValuePair<string, object?>("mcp.server.name", serverName),
            new KeyValuePair<string, object?>("mcp.tool.name", toolName));

        if (result.IsError)
        {
            TelemetryMetrics.ToolCallErrors.Add(1,
                new KeyValuePair<string, object?>("mcp.server.name", serverName),
                new KeyValuePair<string, object?>("mcp.tool.name", toolName));
        }

        await EmitAuditAsync(
            server,
            tool,
            context,
            argumentsJson,
            result,
            (int)response.StatusCode,
            latencyMs,
            ct);

        return result;
    }

    private async Task EmitAuditAsync(
        McpServerDefinition server,
        ToolDefinition tool,
        ToolCallContext? context,
        string argumentsJson,
        ToolCallResult result,
        int httpStatus,
        long latencyMs,
        CancellationToken ct)
    {
        if (context is null)
        {
            return;
        }

        var responseText = result.Content.FirstOrDefault()?.Text ?? string.Empty;

        var auditEvent = new AuditEvent
        {
            CallerId = context.Caller.Id,
            CallerName = context.Caller.Name,
            GatewayAuthMethod = context.Caller.AuthMethod.ToString(),
            AuthStrategy = server.AuthStrategy,
            ServerName = server.Name,
            ToolName = tool.ToolName,
            Arguments = argumentsJson,
            Response = responseText,
            HttpStatus = httpStatus,
            IsError = result.IsError,
            LatencyMs = latencyMs
        };

        await _auditEmitter.EmitAsync(auditEvent, ct);
    }
}
