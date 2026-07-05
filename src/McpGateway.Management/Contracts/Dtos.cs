using System.Text.Json.Nodes;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.Management.Contracts;

public record CreateServerRequest(
    string Name,
    string DisplayName,
    string? Description,
    string? SpecSourceUrl,
    string? SpecContent,
    string BaseUrl,
    string AuthStrategy,
    JsonObject AuthConfig,
    string ToolMode,
    string ClientProfile,
    int PollIntervalMinutes,
    string? CreatedBy);

public record UpdateServerRequest(
    string? DisplayName,
    string? Description,
    string? SpecSourceUrl,
    string? BaseUrl,
    string? AuthStrategy,
    JsonObject? AuthConfig,
    string? ToolMode,
    string? ClientProfile,
    int? PollIntervalMinutes,
    string? Status);

public record ServerResponse(
    Guid Id,
    string Name,
    string DisplayName,
    string? Description,
    string? SpecSourceUrl,
    string BaseUrl,
    string AuthStrategy,
    JsonObject AuthConfig,
    string ToolMode,
    string ClientProfile,
    int PollIntervalMinutes,
    string Status,
    string ApprovalStatus,
    DateTime? ApprovedAt,
    string? ApprovedBy,
    DateTime? LastRefreshedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<SpecValidationIssue>? Warnings = null)
{
    public static ServerResponse FromDomain(McpServerDefinition d, IReadOnlyList<SpecValidationIssue>? warnings = null) => new(
        d.Id, d.Name, d.DisplayName, d.Description, d.SpecSourceUrl, d.BaseUrl,
        d.AuthStrategy, ParseJsonObject(d.AuthConfig), d.ToolMode.ToString().ToLowerInvariant(),
        d.ClientProfile.ToString().ToLowerInvariant(), d.PollIntervalMinutes, d.Status,
        d.ApprovalStatus, d.ApprovedAt, d.ApprovedBy, d.LastRefreshedAt, d.CreatedAt, d.UpdatedAt,
        warnings);

    private static JsonObject ParseJsonObject(string raw)
    {
        try { return JsonNode.Parse(raw) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }
}

public record ToolResponse(
    string ToolName,
    string Description,
    string? HttpMethod,
    string? HttpPath,
    JsonObject InputSchema,
    JsonObject? OutputSchema,
    bool Visible,
    bool HasOverride,
    string EffectiveDescription);

public record UpdateToolRequest(
    string? DescriptionOverride,
    bool? Visible);

public record PutOverrideRequest(string DescriptionOverride);

public record ApiKeySummary(
    Guid Id,
    string KeyPrefix,
    string Name,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAt,
    DateTime? RevokedAt,
    DateTime? LastUsedAt);

public record CreateApiKeyRequest(string Name, IReadOnlyList<string> Scopes);

public record CreateApiKeyResponse(
    Guid Id,
    string KeyPrefix,
    string Name,
    IReadOnlyList<string> Scopes,
    DateTime CreatedAt,
    string FullKey);

public record SpecUploadRequest(string Content, string? ContentType);

public record SpecSourceUpdateRequest(string SpecSourceUrl);

public record SpecDiffResponse(
    string CurrentHash,
    string ComparedHash,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<ToolChangeDto> Changed);

public record ToolChangeDto(
    string ToolName,
    string HttpMethod,
    string HttpPath,
    IReadOnlyList<string> ChangedFields);

public record ApproveResponse(Guid Id, string ApprovalStatus, DateTime ApprovedAt, string ApprovedBy, int ToolCount);

public record RefreshResponse(
    Guid Id,
    string ApprovalStatus,
    bool SpecChanged,
    string SpecHash,
    DateTime LastRefreshedAt,
    int ToolCount,
    IReadOnlyList<SpecValidationIssue>? Warnings = null);

public record ErrorResponse(string Error, string? Detail = null);
