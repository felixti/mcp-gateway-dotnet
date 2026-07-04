namespace McpGateway.Core.ServerDefinitions;

public class GatewayApiKey
{
    public Guid Id { get; set; }
    public Guid ServerDefinitionId { get; set; }
    public string KeyHash { get; set; } = null!;
    public string KeyPrefix { get; set; } = null!;
    public string Name { get; set; } = null!;
    public IReadOnlyList<string> Scopes { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public McpServerDefinition ServerDefinition { get; set; } = null!;
}
