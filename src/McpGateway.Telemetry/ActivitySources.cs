using System.Diagnostics;

namespace McpGateway.Telemetry;

public static class ActivitySources
{
    public const string ToolCallName = "McpGateway.ToolCall";
    public const string OboExchangeName = "McpGateway.OboExchange";

    public static readonly ActivitySource ToolCall = new(ToolCallName, "1.0.0");
    public static readonly ActivitySource OboExchange = new(OboExchangeName, "1.0.0");
}
