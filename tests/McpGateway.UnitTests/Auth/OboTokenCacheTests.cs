using FluentAssertions;
using McpGateway.Core.Auth;
using Microsoft.Extensions.Caching.Memory;

namespace McpGateway.UnitTests.Auth;

public class OboTokenCacheTests
{
    private readonly OboTokenCache _cache = new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        _cache.Get("token", "resource").Should().BeNull();
    }

    [Fact]
    public void SetAndGet_ReturnsToken()
    {
        _cache.Set("token", "resource", "cached-token", DateTimeOffset.UtcNow.AddHours(1));
        _cache.Get("token", "resource").Should().Be("cached-token");
    }
}
