using Microsoft.Extensions.Caching.Memory;

namespace McpGateway.Core.Auth;

public class OboTokenCache
{
    private readonly IMemoryCache _cache;

    public OboTokenCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public string? Get(string callerToken, string resource)
    {
        var key = BuildKey(callerToken, resource);
        return _cache.Get<string>(key);
    }

    public void Set(string callerToken, string resource, string accessToken, DateTimeOffset expiresAt)
    {
        var key = BuildKey(callerToken, resource);
        var buffer = TimeSpan.FromMinutes(5);
        var absoluteExpiration = expiresAt - buffer;
        _cache.Set(key, accessToken, absoluteExpiration);
    }

    private static string BuildKey(string callerToken, string resource)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(callerToken + resource));
        return Convert.ToHexString(hash);
    }
}
