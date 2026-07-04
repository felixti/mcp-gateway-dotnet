using System.Text.Json.Nodes;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;

namespace McpGateway.Management.Services;

public class ToolManagementService
{
    private readonly IServerDefinitionRepository _serverRepo;
    private readonly IToolOverrideRepository _overrideRepo;

    public ToolManagementService(
        IServerDefinitionRepository serverRepo,
        IToolOverrideRepository overrideRepo)
    {
        _serverRepo = serverRepo;
        _overrideRepo = overrideRepo;
    }

    public async Task<IReadOnlyList<ToolResponse>> ListAsync(string serverName, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var overrides = await _overrideRepo.ListByServerAsync(def.Id, ct);
        var byName = overrides.ToDictionary(o => o.ToolName, StringComparer.Ordinal);

        return def.Tools
            .OrderBy(t => t.ToolName, StringComparer.Ordinal)
            .Select(t =>
            {
                var hasOverride = byName.TryGetValue(t.ToolName, out var ov);
                var effective = hasOverride ? ov!.DescriptionOverride : t.Description;
                var visible = hasOverride ? ov!.Visible : t.Visible;
                return new ToolResponse(
                    t.ToolName,
                    t.Description,
                    t.HttpMethod,
                    t.HttpPath,
                    ParseJsonObject(t.InputSchema),
                    ParseJsonObject(t.OutputSchema),
                    visible,
                    hasOverride,
                    effective);
            })
            .ToList();
    }

    public async Task UpdateAsync(string serverName, string toolName, UpdateToolRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var tool = def.Tools.FirstOrDefault(t => string.Equals(t.ToolName, toolName, StringComparison.Ordinal))
            ?? throw new NotFoundException("Tool", toolName);

        var existing = await _overrideRepo.GetAsync(def.Id, toolName, ct);

        var entry = existing ?? new ToolOverride
        {
            ServerDefinitionId = def.Id,
            ToolName = toolName,
            DescriptionOverride = tool.Description,
            Visible = tool.Visible,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (request.DescriptionOverride is not null) entry.DescriptionOverride = request.DescriptionOverride;
        if (request.Visible is not null) entry.Visible = request.Visible.Value;
        entry.UpdatedAt = DateTime.UtcNow;

        await _overrideRepo.UpsertAsync(entry, ct);
    }

    public async Task PutOverrideAsync(string serverName, string toolName, PutOverrideRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var tool = def.Tools.FirstOrDefault(t => string.Equals(t.ToolName, toolName, StringComparison.Ordinal))
            ?? throw new NotFoundException("Tool", toolName);

        var existing = await _overrideRepo.GetAsync(def.Id, toolName, ct);
        var entry = existing ?? new ToolOverride
        {
            ServerDefinitionId = def.Id,
            ToolName = toolName,
            Visible = tool.Visible,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        entry.DescriptionOverride = request.DescriptionOverride;
        entry.Visible = tool.Visible;
        entry.UpdatedAt = DateTime.UtcNow;
        await _overrideRepo.UpsertAsync(entry, ct);
    }

    public async Task DeleteOverrideAsync(string serverName, string toolName, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);
        await _overrideRepo.DeleteAsync(def.Id, toolName, ct);
    }

    private static JsonObject ParseJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new JsonObject();
        try { return JsonNode.Parse(raw) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }
}
