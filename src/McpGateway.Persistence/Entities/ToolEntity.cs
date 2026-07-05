namespace McpGateway.Persistence.Entities;

public class ToolEntity
{
    public Guid Id { get; set; }
    public Guid ServerDefinitionId { get; set; }
    public string ToolName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? HttpMethod { get; set; }
    public string? HttpPath { get; set; }
    public string InputSchema { get; set; } = null!;
    public string? OutputSchema { get; set; }
    public string AuthConfig { get; set; } = "{}";
    public bool Visible { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public McpServerDefinitionEntity ServerDefinition { get; set; } = null!;
}
