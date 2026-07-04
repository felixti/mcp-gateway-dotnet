using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace McpGateway.Persistence.Repositories;

public class SpecVersionRepository : ISpecVersionRepository
{
    private readonly McpGatewayDbContext _context;

    public SpecVersionRepository(McpGatewayDbContext context)
    {
        _context = context;
    }

    public async Task<SpecVersion> AddAsync(SpecVersion version, CancellationToken ct = default)
    {
        var entity = new SpecVersionEntity
        {
            Id = version.Id == Guid.Empty ? Guid.NewGuid() : version.Id,
            ServerDefinitionId = version.ServerDefinitionId,
            SpecHash = version.SpecHash,
            SpecContent = version.SpecContent,
            ToolCount = version.ToolCount,
            DiffSummary = version.DiffSummary,
            CreatedAt = DateTime.UtcNow
        };

        _context.SpecVersions.Add(entity);
        await _context.SaveChangesAsync(ct);

        return ToDomain(entity);
    }

    public async Task<SpecVersion?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.SpecVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, ct);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyList<SpecVersion>> ListByServerAsync(Guid serverDefinitionId, int limit = 50, CancellationToken ct = default)
    {
        var entities = await _context.SpecVersions
            .AsNoTracking()
            .Where(v => v.ServerDefinitionId == serverDefinitionId)
            .OrderByDescending(v => v.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return entities.Select(ToDomain).ToList();
    }

    public async Task<SpecVersion?> GetLatestAsync(Guid serverDefinitionId, CancellationToken ct = default)
    {
        var entity = await _context.SpecVersions
            .AsNoTracking()
            .Where(v => v.ServerDefinitionId == serverDefinitionId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : ToDomain(entity);
    }

    private static SpecVersion ToDomain(SpecVersionEntity entity) => new()
    {
        Id = entity.Id,
        ServerDefinitionId = entity.ServerDefinitionId,
        SpecHash = entity.SpecHash,
        SpecContent = entity.SpecContent,
        ToolCount = entity.ToolCount,
        DiffSummary = entity.DiffSummary,
        CreatedAt = entity.CreatedAt
    };
}
