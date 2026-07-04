using System.Security.Claims;
using System.Text.Encodings.Web;
using McpGateway.Core.Auth;
using McpGateway.Core.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace McpGateway.Api.Auth;

public class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "GatewayApiKey";
    public const string HeaderName = "X-Gateway-Key";
    public const string KeyPrefix = "mgk_";
    public const int KeyPrefixLength = 12;

    private readonly IGatewayApiKeyRepository _apiKeyRepository;

    public ApiKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IGatewayApiKeyRepository apiKeyRepository)
        : base(options, logger, encoder)
    {
        _apiKeyRepository = apiKeyRepository;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValue))
        {
            return AuthenticateResult.NoResult();
        }

        var providedKey = headerValue.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(providedKey) ||
            !providedKey.StartsWith(KeyPrefix) ||
            providedKey.Length < KeyPrefixLength)
        {
            return AuthenticateResult.Fail("Invalid API key format.");
        }

        var prefix = providedKey[..KeyPrefixLength];
        var keyRecord = await _apiKeyRepository.GetByPrefixAsync(prefix);

        if (keyRecord is null ||
            !BCrypt.Net.BCrypt.Verify(providedKey, keyRecord.KeyHash) ||
            !keyRecord.Scopes.Contains(Context.Request.RouteValues["serverName"]?.ToString() ?? string.Empty))
        {
            return AuthenticateResult.Fail("Invalid or unauthorized API key.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, keyRecord.Id.ToString()),
            new Claim(ClaimTypes.Name, keyRecord.Name),
            new Claim("auth_method", GatewayAuthMethod.GatewayApiKey.ToString())
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
