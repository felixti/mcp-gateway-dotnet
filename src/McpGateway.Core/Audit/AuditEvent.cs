using System.Text.Json.Serialization;

namespace McpGateway.Core.Audit;

public class AuditEvent
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("caller_id")]
    public string CallerId { get; set; } = string.Empty;

    [JsonPropertyName("caller_name")]
    public string? CallerName { get; set; }

    [JsonPropertyName("gateway_auth_method")]
    public string GatewayAuthMethod { get; set; } = string.Empty;

    [JsonPropertyName("auth_strategy")]
    public string AuthStrategy { get; set; } = string.Empty;

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";

    [JsonPropertyName("response")]
    public string Response { get; set; } = string.Empty;

    [JsonPropertyName("http_status")]
    public int HttpStatus { get; set; }

    [JsonPropertyName("is_error")]
    public bool IsError { get; set; }

    [JsonPropertyName("latency_ms")]
    public long LatencyMs { get; set; }
}
