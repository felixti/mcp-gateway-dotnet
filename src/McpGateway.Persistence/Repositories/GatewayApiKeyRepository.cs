using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using Microsoft.EntityFrameworkCore;

namespace McpGateway.Persistence.Repositories;

public class GatewayApiKeyRepository : IGatewayApiKeyRepository
{
    private readonly McpGatewayDbContext _context;

    public GatewayApiKeyRepository(McpGatewayDbContext context)
    {
        _context = context;
    }

    public async Task<GatewayApiKey?> GetByPrefixAsync(string keyPrefix, CancellationToken ct = default)
    {
        var entity = await _context.GatewayApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyPrefix == keyPrefix && k.RevokedAt == null, ct);

        return entity?.ToDomain();
    }

    public async Task<IReadOnlyList<GatewayApiKey>> ListByServerAsync(Guid serverDefinitionId, CancellationToken ct = default)
    {
        var entities = await _context.GatewayApiKeys
            .AsNoTracking()
            .Where(k => k.ServerDefinitionId == serverDefinitionId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<GatewayApiKey> AddAsync(GatewayApiKey apiKey, CancellationToken ct = default)
    {
        var entity = apiKey.ToEntity();
        entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
        entity.CreatedAt = DateTime.UtcNow;

        _context.GatewayApiKeys.Add(entity);
        await _context.SaveChangesAsync(ct);

        return entity.ToDomain();
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _context.GatewayApiKeys.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            entity.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }
}
