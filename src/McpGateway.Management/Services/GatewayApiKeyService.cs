using System.Security.Cryptography;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;

namespace McpGateway.Management.Services;

public class GatewayApiKeyService
{
    public const string KeyPrefix = "mgk_";
    private const int FullKeyByteLength = 32;
    private const int WorkFactor = 11;

    private readonly IGatewayApiKeyRepository _keyRepo;
    private readonly IServerDefinitionRepository _serverRepo;

    public GatewayApiKeyService(IGatewayApiKeyRepository keyRepo, IServerDefinitionRepository serverRepo)
    {
        _keyRepo = keyRepo;
        _serverRepo = serverRepo;
    }

    public async Task<CreateApiKeyResponse> IssueAsync(string serverName, CreateApiKeyRequest request, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var fullKey = GenerateFullKey();
        var keyHash = BCrypt.Net.BCrypt.HashPassword(fullKey, WorkFactor);
        var keyPrefix = fullKey[..12];

        var entry = new GatewayApiKey
        {
            Id = Guid.NewGuid(),
            ServerDefinitionId = def.Id,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            Name = request.Name,
            Scopes = request.Scopes.ToList(),
            CreatedAt = DateTime.UtcNow
        };

        var saved = await _keyRepo.AddAsync(entry, ct);

        return new CreateApiKeyResponse(
            saved.Id, saved.KeyPrefix, saved.Name, saved.Scopes, saved.CreatedAt, fullKey);
    }

    public async Task<IReadOnlyList<ApiKeySummary>> ListAsync(string serverName, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        var keys = await _keyRepo.ListByServerAsync(def.Id, ct);
        return keys
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeySummary(
                k.Id, k.KeyPrefix, k.Name, k.Scopes, k.CreatedAt, k.RevokedAt, k.LastUsedAt))
            .ToList();
    }

    public Task RevokeAsync(string serverName, Guid keyId, CancellationToken ct)
    {
        return _keyRepo.RevokeAsync(keyId, ct);
    }

    private static string GenerateFullKey()
    {
        Span<byte> bytes = stackalloc byte[FullKeyByteLength];
        RandomNumberGenerator.Fill(bytes);
        var base64 = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return $"{KeyPrefix}{base64}";
    }
}
