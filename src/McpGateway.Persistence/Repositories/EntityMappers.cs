using McpGateway.Core.ServerDefinitions;
using McpGateway.Persistence.Entities;

namespace McpGateway.Persistence.Repositories;

internal static class EntityMappers
{
    public static McpServerDefinition ToDomain(this McpServerDefinitionEntity entity)
    {
        return new McpServerDefinition
        {
            Id = entity.Id,
            Name = entity.Name,
            DisplayName = entity.DisplayName,
            Description = entity.Description,
            SpecSourceUrl = entity.SpecSourceUrl,
            SpecContent = entity.SpecContent,
            SpecHash = entity.SpecHash,
            BaseUrl = entity.BaseUrl,
            AuthStrategy = entity.AuthStrategy,
            AuthConfig = entity.AuthConfig,
            ToolMode = entity.ToolMode,
            ClientProfile = entity.ClientProfile,
            SourceType = entity.SourceType,
            PollIntervalMinutes = entity.PollIntervalMinutes,
            Status = entity.Status,
            ApprovalStatus = entity.ApprovalStatus,
            ApprovedAt = entity.ApprovedAt,
            ApprovedBy = entity.ApprovedBy,
            LastRefreshedAt = entity.LastRefreshedAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            Tools = entity.Tools.Select(ToDomain).ToList(),
            ToolOverrides = entity.ToolOverrides.Select(ToDomain).ToList(),
            GatewayApiKeys = entity.GatewayApiKeys.Select(ToDomain).ToList(),
            SpecVersions = entity.SpecVersions.Select(ToDomain).ToList()
        };
    }

    public static ToolDefinition ToDomain(this ToolEntity entity) => new()
    {
        Id = entity.Id,
        ServerDefinitionId = entity.ServerDefinitionId,
        ToolName = entity.ToolName,
        Description = entity.Description,
        HttpMethod = entity.HttpMethod,
        HttpPath = entity.HttpPath,
        InputSchema = entity.InputSchema,
        OutputSchema = entity.OutputSchema,
        AuthConfig = entity.AuthConfig,
        Visible = entity.Visible,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };

    public static ToolOverride ToDomain(this ToolOverrideEntity entity) => new()
    {
        Id = entity.Id,
        ServerDefinitionId = entity.ServerDefinitionId,
        ToolName = entity.ToolName,
        DescriptionOverride = entity.DescriptionOverride,
        Visible = entity.Visible,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };

    public static GatewayApiKey ToDomain(this GatewayApiKeyEntity entity) => new()
    {
        Id = entity.Id,
        ServerDefinitionId = entity.ServerDefinitionId,
        KeyHash = entity.KeyHash,
        KeyPrefix = entity.KeyPrefix,
        Name = entity.Name,
        Scopes = entity.Scopes.AsReadOnly(),
        CreatedAt = entity.CreatedAt,
        RevokedAt = entity.RevokedAt,
        LastUsedAt = entity.LastUsedAt
    };

    public static SpecVersion ToDomain(this SpecVersionEntity entity) => new()
    {
        Id = entity.Id,
        ServerDefinitionId = entity.ServerDefinitionId,
        SpecHash = entity.SpecHash,
        SpecContent = entity.SpecContent,
        ToolCount = entity.ToolCount,
        DiffSummary = entity.DiffSummary,
        CreatedAt = entity.CreatedAt
    };

    public static McpServerDefinitionEntity ToEntity(this McpServerDefinition domain)
    {
        return new McpServerDefinitionEntity
        {
            Id = domain.Id,
            Name = domain.Name,
            DisplayName = domain.DisplayName,
            Description = domain.Description,
            SpecSourceUrl = domain.SpecSourceUrl,
            SpecContent = domain.SpecContent,
            SpecHash = domain.SpecHash,
            BaseUrl = domain.BaseUrl,
            AuthStrategy = domain.AuthStrategy,
            AuthConfig = domain.AuthConfig,
            ToolMode = domain.ToolMode,
            ClientProfile = domain.ClientProfile,
            SourceType = domain.SourceType,
            PollIntervalMinutes = domain.PollIntervalMinutes,
            Status = domain.Status,
            ApprovalStatus = domain.ApprovalStatus,
            ApprovedAt = domain.ApprovedAt,
            ApprovedBy = domain.ApprovedBy,
            LastRefreshedAt = domain.LastRefreshedAt,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
            Tools = domain.Tools.Select(ToEntity).ToList(),
            ToolOverrides = domain.ToolOverrides.Select(ToEntity).ToList(),
            GatewayApiKeys = domain.GatewayApiKeys.Select(ToEntity).ToList(),
            SpecVersions = domain.SpecVersions.Select(ToEntity).ToList()
        };
    }

    public static ToolEntity ToEntity(this ToolDefinition domain) => new()
    {
        Id = domain.Id,
        ServerDefinitionId = domain.ServerDefinitionId,
        ToolName = domain.ToolName,
        Description = domain.Description,
        HttpMethod = domain.HttpMethod,
        HttpPath = domain.HttpPath,
        InputSchema = domain.InputSchema,
        OutputSchema = domain.OutputSchema,
        AuthConfig = domain.AuthConfig,
        Visible = domain.Visible,
        CreatedAt = domain.CreatedAt,
        UpdatedAt = domain.UpdatedAt
    };

    public static ToolOverrideEntity ToEntity(this ToolOverride domain) => new()
    {
        Id = domain.Id,
        ServerDefinitionId = domain.ServerDefinitionId,
        ToolName = domain.ToolName,
        DescriptionOverride = domain.DescriptionOverride,
        Visible = domain.Visible,
        CreatedAt = domain.CreatedAt,
        UpdatedAt = domain.UpdatedAt
    };

    public static GatewayApiKeyEntity ToEntity(this GatewayApiKey domain) => new()
    {
        Id = domain.Id,
        ServerDefinitionId = domain.ServerDefinitionId,
        KeyHash = domain.KeyHash,
        KeyPrefix = domain.KeyPrefix,
        Name = domain.Name,
        Scopes = domain.Scopes.ToList(),
        CreatedAt = domain.CreatedAt,
        RevokedAt = domain.RevokedAt,
        LastUsedAt = domain.LastUsedAt
    };

    public static SpecVersionEntity ToEntity(this SpecVersion domain) => new()
    {
        Id = domain.Id,
        ServerDefinitionId = domain.ServerDefinitionId,
        SpecHash = domain.SpecHash,
        SpecContent = domain.SpecContent,
        ToolCount = domain.ToolCount,
        DiffSummary = domain.DiffSummary,
        CreatedAt = domain.CreatedAt
    };
}
