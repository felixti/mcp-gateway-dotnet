namespace McpGateway.Core.ServerDefinitions;

public class SpecVersion
{
    public Guid Id { get; set; }
    public Guid ServerDefinitionId { get; set; }
    public string SpecHash { get; set; } = null!;
    public string SpecContent { get; set; } = "{}";
    public int ToolCount { get; set; }
    public string DiffSummary { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }

    public McpServerDefinition ServerDefinition { get; set; } = null!;
}
