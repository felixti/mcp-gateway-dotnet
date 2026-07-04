using System.Text.Json;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Auth;

public class AuthStrategyResolver
{
    private readonly IOboTokenExchange _oboTokenExchange;
    private readonly OboTokenCache _oboTokenCache;

    public AuthStrategyResolver(IOboTokenExchange oboTokenExchange, OboTokenCache oboTokenCache)
    {
        _oboTokenExchange = oboTokenExchange;
        _oboTokenCache = oboTokenCache;
    }

    public async Task<string?> ResolveAuthorizationHeaderAsync(
        McpServerDefinition server,
        CallerIdentity caller,
        CancellationToken ct = default)
    {
        var authConfig = JsonSerializer.Deserialize<Dictionary<string, string>>(server.AuthConfig) ?? [];

        return server.AuthStrategy.ToLowerInvariant() switch
        {
            "obo" => await ResolveOboTokenAsync(caller, authConfig, ct),
            "passthrough" => $"Bearer {GetCallerJwt(caller)}",
            "static" => $"Bearer {authConfig.GetValueOrDefault("api_key")}",
            _ => throw new NotSupportedException($"Auth strategy '{server.AuthStrategy}' is not supported.")
        };
    }

    private async Task<string?> ResolveOboTokenAsync(CallerIdentity caller, Dictionary<string, string> authConfig, CancellationToken ct)
    {
        var callerJwt = GetCallerJwt(caller);
        var resource = authConfig.GetValueOrDefault("resource") ?? throw new InvalidOperationException("OBO resource is required.");

        var cached = _oboTokenCache.Get(callerJwt, resource);
        if (cached is not null)
        {
            return $"Bearer {cached}";
        }

        var exchanged = await _oboTokenExchange.ExchangeAsync(callerJwt, resource, ct);
        _oboTokenCache.Set(callerJwt, resource, exchanged, DateTimeOffset.UtcNow.AddHours(1));
        return $"Bearer {exchanged}";
    }

    private static string GetCallerJwt(CallerIdentity caller)
    {
        return caller.GetType().GetProperty("RawToken")?.GetValue(caller)?.ToString() ?? string.Empty;
    }
}
