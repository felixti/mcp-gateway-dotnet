using System.Diagnostics.Metrics;

namespace McpGateway.Telemetry;

public static class TelemetryMetrics
{
    public const string MeterName = "McpGateway";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> ToolCallCount =
        Meter.CreateCounter<long>("mcp.tool.call.count", description: "Total MCP tool calls.");

    public static readonly Histogram<double> ToolCallLatencyMs =
        Meter.CreateHistogram<double>("mcp.tool.call.latency.ms", unit: "ms", description: "MCP tool call latency in milliseconds.");

    public static readonly Counter<long> ToolCallErrors =
        Meter.CreateCounter<long>("mcp.tool.call.errors", description: "MCP tool calls that returned isError=true.");

    public static readonly Counter<long> OboCacheHits =
        Meter.CreateCounter<long>("mcp.obo.cache.hits", description: "OBO token cache hits.");

    public static readonly Counter<long> OboCacheMisses =
        Meter.CreateCounter<long>("mcp.obo.cache.misses", description: "OBO token cache misses.");
}
