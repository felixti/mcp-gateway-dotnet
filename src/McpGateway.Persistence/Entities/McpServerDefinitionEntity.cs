using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Persistence.Entities;

public class McpServerDefinitionEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? SpecSourceUrl { get; set; }
    public string SpecContent { get; set; } = null!;
    public string SpecHash { get; set; } = null!;
    public string BaseUrl { get; set; } = null!;
    public string AuthStrategy { get; set; } = "obo";
    public string AuthConfig { get; set; } = "{}";
    public ToolMode ToolMode { get; set; } = ToolMode.All;
    public ClientProfile ClientProfile { get; set; } = ClientProfile.Universal;
    public SourceType SourceType { get; set; } = SourceType.OpenApi;
    public int PollIntervalMinutes { get; set; } = 1440;
    public string Status { get; set; } = "active";
    public string ApprovalStatus { get; set; } = "pending";
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? LastRefreshedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ToolEntity> Tools { get; set; } = [];
    public ICollection<ToolOverrideEntity> ToolOverrides { get; set; } = [];
    public ICollection<GatewayApiKeyEntity> GatewayApiKeys { get; set; } = [];
    public ICollection<SpecVersionEntity> SpecVersions { get; set; } = [];
}
