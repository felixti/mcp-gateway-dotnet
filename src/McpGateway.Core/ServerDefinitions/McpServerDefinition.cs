namespace McpGateway.Core.ServerDefinitions;

public class McpServerDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? SpecSourceUrl { get; set; }
    public string SpecContent { get; set; } = "{}";
    public string SpecHash { get; set; } = null!;
    public string BaseUrl { get; set; } = null!;
    public string AuthStrategy { get; set; } = "obo";
    public string AuthConfig { get; set; } = "{}";
    public ToolMode ToolMode { get; set; } = ToolMode.All;
    public ClientProfile ClientProfile { get; set; } = ClientProfile.Universal;
    public int PollIntervalMinutes { get; set; } = 1440;
    public string Status { get; set; } = "active";
    public string ApprovalStatus { get; set; } = "pending";
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? LastRefreshedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ToolDefinition> Tools { get; set; } = [];
    public ICollection<ToolOverride> ToolOverrides { get; set; } = [];
    public ICollection<GatewayApiKey> GatewayApiKeys { get; set; } = [];
    public ICollection<SpecVersion> SpecVersions { get; set; } = [];
}
