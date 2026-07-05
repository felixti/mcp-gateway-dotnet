using System.Text.Json.Nodes;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.McpUpstream;

public sealed class UpstreamCatalogImporter
{
    public IReadOnlyList<ToolDefinition> Import(
        IReadOnlyList<UpstreamTool> tools,
        Guid serverId)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var now = DateTime.UtcNow;

        return tools.Select(tool => new ToolDefinition
        {
            Id = Guid.NewGuid(),
            ServerDefinitionId = serverId,
            ToolName = tool.Name,
            Description = tool.Description ?? string.Empty,
            HttpMethod = null,
            HttpPath = null,
            InputSchema = tool.InputSchema?.ToJsonString() ?? "{}",
            OutputSchema = null,
            AuthConfig = "{}",
            Visible = true,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
    }
}
