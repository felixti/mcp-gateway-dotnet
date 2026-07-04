using System.Security.Claims;
using System.Text.Encodings.Web;
using McpGateway.Management.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace McpGateway.Api.Auth;

public class DevelopmentAdminOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-Dev-Admin";
    public string DefaultUpn { get; set; } = "dev-admin@corp.local";
}

public class DevelopmentAdminAuthHandler : AuthenticationHandler<DevelopmentAdminOptions>
{
    public const string SchemeName = "DevAdmin";

    public DevelopmentAdminAuthHandler(
        IOptionsMonitor<DevelopmentAdminOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var v) || string.IsNullOrWhiteSpace(v.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var upn = v.ToString();

        var claims = new[]
        {
            new Claim(CallerIdentityAccessor.AdminUpnClaim, upn),
            new Claim(ClaimTypes.Role, CallerIdentityAccessor.AdminRole),
            new Claim(ClaimTypes.NameIdentifier, upn)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
