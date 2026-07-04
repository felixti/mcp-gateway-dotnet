using System.Text.Encodings.Web;
using FluentAssertions;
using McpGateway.Api.Auth;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace McpGateway.UnitTests.Auth;

public class ApiKeyAuthHandlerTests
{
    [Fact]
    public async Task MissingHeader_ReturnsNoResult()
    {
        var handler = CreateHandler();
        var context = new DefaultHttpContext();
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyAuthHandler.SchemeName, null, typeof(ApiKeyAuthHandler)), context);

        var result = await handler.AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task ValidKey_ReturnsSuccess()
    {
        var fullKey = "mgk_validkey123";
        var prefix = fullKey[..ApiKeyAuthHandler.KeyPrefixLength];
        var keyHash = BCrypt.Net.BCrypt.HashPassword(fullKey);
        var keyRecord = new GatewayApiKey
        {
            Id = Guid.NewGuid(),
            Name = "Test Key",
            KeyHash = keyHash,
            KeyPrefix = prefix,
            Scopes = ["invoice-api"]
        };

        var repo = new Mock<IGatewayApiKeyRepository>();
        repo.Setup(r => r.GetByPrefixAsync(prefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(keyRecord);

        var context = new DefaultHttpContext();
        context.Request.RouteValues["serverName"] = "invoice-api";
        context.Request.Headers[ApiKeyAuthHandler.HeaderName] = fullKey;

        var handler = CreateHandler(repo.Object);
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyAuthHandler.SchemeName, null, typeof(ApiKeyAuthHandler)), context);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal?.Identity?.Name.Should().Be("Test Key");
    }

    [Fact]
    public async Task InvalidKey_ReturnsFail()
    {
        var fullKey = "mgk_validkey123";
        var wrongKey = "mgk_validkey999";
        var prefix = fullKey[..ApiKeyAuthHandler.KeyPrefixLength];
        var keyRecord = new GatewayApiKey
        {
            Id = Guid.NewGuid(),
            Name = "Test Key",
            KeyHash = BCrypt.Net.BCrypt.HashPassword(fullKey),
            KeyPrefix = prefix,
            Scopes = ["invoice-api"]
        };

        var repo = new Mock<IGatewayApiKeyRepository>();
        repo.Setup(r => r.GetByPrefixAsync(prefix, It.IsAny<CancellationToken>()))
            .ReturnsAsync(keyRecord);

        var context = new DefaultHttpContext();
        context.Request.RouteValues["serverName"] = "invoice-api";
        context.Request.Headers[ApiKeyAuthHandler.HeaderName] = wrongKey;

        var handler = CreateHandler(repo.Object);
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyAuthHandler.SchemeName, null, typeof(ApiKeyAuthHandler)), context);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
    }

    private static ApiKeyAuthHandler CreateHandler(IGatewayApiKeyRepository? repo = null)
    {
        var options = new AuthenticationSchemeOptions();
        var optionsMonitor = new TestOptionsMonitor<AuthenticationSchemeOptions>(options);
        return new ApiKeyAuthHandler(
            optionsMonitor,
            LoggerFactory.Create(b => { }),
            UrlEncoder.Default,
            repo ?? Mock.Of<IGatewayApiKeyRepository>());
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public TestOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
