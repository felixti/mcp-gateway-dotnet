using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using McpGateway.Core.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace McpGateway.Api.Auth;

public class JwtAuthHandler : AuthenticationHandler<JwtAuthOptions>
{
    public const string SchemeName = "EntraIdJwt";

    private readonly IConfiguration _configuration;

    public JwtAuthHandler(
        IOptionsMonitor<JwtAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.Authorization.ToString().StartsWith("Bearer "))
        {
            return AuthenticateResult.NoResult();
        }

        var token = Request.Headers.Authorization.ToString()["Bearer ".Length..].Trim();

        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://login.microsoftonline.com/{Options.TenantId}/v2.0",
                ValidateAudience = true,
                ValidAudience = Options.Audience,
                ValidateLifetime = true,
                IssuerSigningKeyResolver = (token, secToken, kid, ct) =>
                {
                    var jwks = new JsonWebKeySet(Options.JwksJson);
                    return jwks.GetSigningKeys();
                }
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParameters, out _);

            var identity = new ClaimsIdentity(principal.Claims, SchemeName);
            identity.AddClaim(new Claim("auth_method", GatewayAuthMethod.EntraIdJwt.ToString()));
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail(ex.Message);
        }
    }
}

public class JwtAuthOptions : AuthenticationSchemeOptions
{
    public string TenantId { get; set; } = null!;
    public string Audience { get; set; } = null!;
    public string JwksUri { get; set; } = null!;
    public string JwksJson { get; set; } = "{}";
}
