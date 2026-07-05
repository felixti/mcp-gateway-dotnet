using System.Text.Json.Nodes;

namespace McpGateway.Core.McpUpstream;

public sealed record UpstreamTool(
    string Name,
    string? Description,
    JsonNode? InputSchema);
