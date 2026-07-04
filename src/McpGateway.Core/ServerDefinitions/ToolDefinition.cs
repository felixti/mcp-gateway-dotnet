namespace McpGateway.Core.ServerDefinitions;

public class ToolDefinition
{
    public Guid Id { get; set; }
    public Guid ServerDefinitionId { get; set; }
    public string ToolName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string HttpMethod { get; set; } = null!;
    public string HttpPath { get; set; } = null!;
    public string InputSchema { get; set; } = "{}";
    public string? OutputSchema { get; set; }
    public string AuthConfig { get; set; } = "{}";
    public bool Visible { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public McpServerDefinition ServerDefinition { get; set; } = null!;
}
