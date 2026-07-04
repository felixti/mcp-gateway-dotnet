namespace McpGateway.Persistence.Entities;

public class SpecVersionEntity
{
    public Guid Id { get; set; }
    public Guid ServerDefinitionId { get; set; }
    public string SpecHash { get; set; } = null!;
    public string SpecContent { get; set; } = null!;
    public int ToolCount { get; set; }
    public string DiffSummary { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }

    public McpServerDefinitionEntity ServerDefinition { get; set; } = null!;
}
