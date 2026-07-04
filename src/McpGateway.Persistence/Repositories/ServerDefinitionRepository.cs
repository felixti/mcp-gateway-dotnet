using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace McpGateway.Persistence.Repositories;

public class ServerDefinitionRepository : IServerDefinitionRepository
{
    private readonly McpGatewayDbContext _context;

    public ServerDefinitionRepository(McpGatewayDbContext context)
    {
        _context = context;
    }

    public async Task<McpServerDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var entity = await _context.ServerDefinitions
            .AsNoTracking()
            .Include(s => s.Tools)
            .Include(s => s.ToolOverrides)
            .FirstOrDefaultAsync(s => s.Name == name, ct);

        return entity?.ToDomain();
    }

    public async Task<McpServerDefinition?> GetByNameForAdminAsync(string name, CancellationToken ct = default)
    {
        var entity = await _context.ServerDefinitions
            .AsNoTracking()
            .Include(s => s.Tools)
            .Include(s => s.ToolOverrides)
            .Include(s => s.GatewayApiKeys)
            .Include(s => s.SpecVersions)
            .FirstOrDefaultAsync(s => s.Name == name, ct);

        return entity?.ToDomain();
    }

    public async Task<IReadOnlyList<McpServerDefinition>> ListAsync(CancellationToken ct = default)
    {
        var entities = await _context.ServerDefinitions
            .AsNoTracking()
            .Include(s => s.Tools)
            .Include(s => s.ToolOverrides)
            .Include(s => s.GatewayApiKeys)
            .Include(s => s.SpecVersions)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<McpServerDefinition>> ListApprovedAsync(CancellationToken ct = default)
    {
        var entities = await _context.ServerDefinitions
            .AsNoTracking()
            .Include(s => s.Tools)
            .Include(s => s.ToolOverrides)
            .Where(s => s.ApprovalStatus == "approved")
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<McpServerDefinition> AddAsync(McpServerDefinition definition, CancellationToken ct = default)
    {
        var entity = definition.ToEntity();
        entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = entity.CreatedAt;

        foreach (var tool in entity.Tools)
        {
            tool.Id = tool.Id == Guid.Empty ? Guid.NewGuid() : tool.Id;
            tool.ServerDefinitionId = entity.Id;
            tool.CreatedAt = entity.CreatedAt;
            tool.UpdatedAt = entity.UpdatedAt;
        }

        _context.ServerDefinitions.Add(entity);
        await _context.SaveChangesAsync(ct);

        return entity.ToDomain();
    }

    public async Task UpdateAsync(McpServerDefinition definition, CancellationToken ct = default)
    {
        var entity = await _context.ServerDefinitions
            .Include(s => s.Tools)
            .FirstOrDefaultAsync(s => s.Id == definition.Id, ct)
            ?? throw new InvalidOperationException($"Server definition {definition.Id} not found.");

        entity.Name = definition.Name;
        entity.DisplayName = definition.DisplayName;
        entity.Description = definition.Description;
        entity.SpecSourceUrl = definition.SpecSourceUrl;
        entity.SpecContent = definition.SpecContent;
        entity.SpecHash = definition.SpecHash;
        entity.BaseUrl = definition.BaseUrl;
        entity.AuthStrategy = definition.AuthStrategy;
        entity.AuthConfig = definition.AuthConfig;
        entity.ToolMode = definition.ToolMode;
        entity.ClientProfile = definition.ClientProfile;
        entity.PollIntervalMinutes = definition.PollIntervalMinutes;
        entity.Status = definition.Status;
        entity.ApprovalStatus = definition.ApprovalStatus;
        entity.ApprovedAt = definition.ApprovedAt;
        entity.ApprovedBy = definition.ApprovedBy;
        entity.LastRefreshedAt = definition.LastRefreshedAt;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateToolsAsync(Guid serverDefinitionId, IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
    {
        var entity = await _context.ServerDefinitions
            .Include(s => s.Tools)
            .FirstOrDefaultAsync(s => s.Id == serverDefinitionId, ct)
            ?? throw new InvalidOperationException($"Server definition {serverDefinitionId} not found.");

        _context.Tools.RemoveRange(entity.Tools);
        await _context.SaveChangesAsync(ct);

        var now = DateTime.UtcNow;
        var newTools = tools.Select(t => new ToolEntity
        {
            Id = t.Id == Guid.Empty ? Guid.NewGuid() : t.Id,
            ServerDefinitionId = serverDefinitionId,
            ToolName = t.ToolName,
            Description = t.Description,
            HttpMethod = t.HttpMethod,
            HttpPath = t.HttpPath,
            InputSchema = t.InputSchema,
            OutputSchema = t.OutputSchema,
            AuthConfig = t.AuthConfig,
            Visible = t.Visible,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        _context.Tools.AddRange(newTools);
        await _context.SaveChangesAsync(ct);

        entity.UpdatedAt = now;
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.ServerDefinitions.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            _context.ServerDefinitions.Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }
}
