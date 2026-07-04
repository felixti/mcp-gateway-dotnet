namespace McpGateway.Core.ServerDefinitions;

public class ToolOverride
{
    public Guid Id { get; set; }
    public Guid ServerDefinitionId { get; set; }
    public string ToolName { get; set; } = null!;
    public string DescriptionOverride { get; set; } = null!;
    public bool Visible { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public McpServerDefinition ServerDefinition { get; set; } = null!;
}
