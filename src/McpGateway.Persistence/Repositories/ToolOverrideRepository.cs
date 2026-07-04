using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using Microsoft.EntityFrameworkCore;

namespace McpGateway.Persistence.Repositories;

public class ToolOverrideRepository : IToolOverrideRepository
{
    private readonly McpGatewayDbContext _context;

    public ToolOverrideRepository(McpGatewayDbContext context)
    {
        _context = context;
    }

    public async Task<ToolOverride?> GetAsync(Guid serverDefinitionId, string toolName, CancellationToken ct = default)
    {
        var entity = await _context.ToolOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.ServerDefinitionId == serverDefinitionId && o.ToolName == toolName, ct);

        return entity?.ToDomain();
    }

    public async Task<IReadOnlyList<ToolOverride>> ListByServerAsync(Guid serverDefinitionId, CancellationToken ct = default)
    {
        var entities = await _context.ToolOverrides
            .AsNoTracking()
            .Where(o => o.ServerDefinitionId == serverDefinitionId)
            .ToListAsync(ct);

        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<ToolOverride> UpsertAsync(ToolOverride overrideEntry, CancellationToken ct = default)
    {
        var existing = await _context.ToolOverrides
            .FirstOrDefaultAsync(
                o => o.ServerDefinitionId == overrideEntry.ServerDefinitionId && o.ToolName == overrideEntry.ToolName,
                ct);

        if (existing is not null)
        {
            existing.DescriptionOverride = overrideEntry.DescriptionOverride;
            existing.Visible = overrideEntry.Visible;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var entity = overrideEntry.ToEntity();
            entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = entity.CreatedAt;
            _context.ToolOverrides.Add(entity);
        }

        await _context.SaveChangesAsync(ct);

        var updated = await _context.ToolOverrides
            .AsNoTracking()
            .FirstAsync(
                o => o.ServerDefinitionId == overrideEntry.ServerDefinitionId && o.ToolName == overrideEntry.ToolName,
                ct);

        return updated.ToDomain();
    }

    public async Task DeleteAsync(Guid serverDefinitionId, string toolName, CancellationToken ct = default)
    {
        var entity = await _context.ToolOverrides
            .FirstOrDefaultAsync(
                o => o.ServerDefinitionId == serverDefinitionId && o.ToolName == toolName,
                ct);

        if (entity is not null)
        {
            _context.ToolOverrides.Remove(entity);
            await _context.SaveChangesAsync(ct);
        }
    }
}
