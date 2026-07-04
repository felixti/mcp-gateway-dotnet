namespace McpGateway.Persistence.Entities;

public class ToolOverrideEntity
{
    public Guid Id { get; set; }
    public Guid ServerDefinitionId { get; set; }
    public string ToolName { get; set; } = null!;
    public string DescriptionOverride { get; set; } = null!;
    public bool Visible { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public McpServerDefinitionEntity ServerDefinition { get; set; } = null!;
}
