using FluentAssertions;
using McpGateway.Core.Auth;
using McpGateway.Core.ServerDefinitions;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace McpGateway.UnitTests.Auth;

public class AuthStrategyResolverTests
{
    [Fact]
    public async Task StaticStrategy_ReturnsApiKeyHeader()
    {
        var resolver = new AuthStrategyResolver(Mock.Of<IOboTokenExchange>(), new OboTokenCache(new MemoryCache(new MemoryCacheOptions())));
        var server = new McpServerDefinition
        {
            Name = "api",
            AuthStrategy = "static",
            AuthConfig = "{\"api_key\":\"secret-key\"}"
        };

        var header = await resolver.ResolveAuthorizationHeaderAsync(server, new CallerIdentity { Id = "user" });

        header.Should().Be("Bearer secret-key");
    }
}
