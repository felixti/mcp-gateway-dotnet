using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.McpUpstream;

public sealed class UpstreamCatalogImporter
{
    public IReadOnlyList<ToolDefinition> Import(
        McpServerDefinition server,
        IReadOnlyList<UpstreamTool> tools)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(tools);

        server.SourceType = SourceType.McpUpstream;

        var now = DateTime.UtcNow;

        return tools.Select(tool => new ToolDefinition
        {
            Id = Guid.NewGuid(),
            ServerDefinitionId = server.Id,
            ServerDefinition = server,
            ToolName = tool.Name,
            Description = tool.Description ?? string.Empty,
            HttpMethod = null,
            HttpPath = null,
            InputSchema = tool.InputSchema ?? "{}",
            OutputSchema = null,
            AuthConfig = "{}",
            Visible = true,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
    }
}
