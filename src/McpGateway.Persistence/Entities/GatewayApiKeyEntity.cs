namespace McpGateway.Persistence.Entities;

public class GatewayApiKeyEntity
{
    public Guid Id { get; set; }
    public Guid ServerDefinitionId { get; set; }
    public string KeyHash { get; set; } = null!;
    public string KeyPrefix { get; set; } = null!;
    public string Name { get; set; } = null!;
    public List<string> Scopes { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public McpServerDefinitionEntity ServerDefinition { get; set; } = null!;
}
