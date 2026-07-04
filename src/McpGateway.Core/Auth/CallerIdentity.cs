namespace McpGateway.Core.Auth;

public class CallerIdentity
{
    public string Id { get; set; } = null!;      // Entra ID oid or API key id
    public string? Name { get; set; }             // UPN or key name
    public string? Email { get; set; }
    public GatewayAuthMethod AuthMethod { get; set; }
}

public enum GatewayAuthMethod
{
    EntraIdJwt,
    GatewayApiKey
}
