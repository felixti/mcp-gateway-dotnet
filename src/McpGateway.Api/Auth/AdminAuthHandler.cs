using System.Security.Claims;
using System.Text.Encodings.Web;
using McpGateway.Management.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace McpGateway.Api.Auth;

public class AdminJwtOptions : AuthenticationSchemeOptions
{
    public string MetadataAddress { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string ValidIssuer { get; set; } = string.Empty;
    public string RoleClaimType { get; set; } = "roles";
    public string UpnClaimType { get; set; } = "preferred_username";
}

public class AdminAuthHandler : AuthenticationHandler<AdminJwtOptions>
{
    public const string SchemeName = "Admin";

    public AdminAuthHandler(
        IOptionsMonitor<AdminJwtOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var auth))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var raw = auth.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = raw["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty bearer token."));
        }

        var claims = DevTokenParser.Parse(token, Options.RoleClaimType, Options.UpnClaimType);
        if (claims.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.Fail("Token could not be parsed."));
        }

        var isAdmin = claims.Any(c =>
            c.Type == ClaimTypes.Role && c.Value == CallerIdentityAccessor.AdminRole);

        if (!isAdmin)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Caller does not have the required '{CallerIdentityAccessor.AdminRole}' role."));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal static class DevTokenParser
{
    public static List<Claim> Parse(string token, string roleClaimType, string upnClaimType)
    {
        var claims = new List<Claim>();
        var parts = token.Split('.');
        if (parts.Length < 2) return claims;

        try
        {
            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            if (root.TryGetProperty(roleClaimType, out var rolesEl))
            {
                if (rolesEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var r in rolesEl.EnumerateArray())
                    {
                        var role = r.GetString();
                        if (!string.IsNullOrEmpty(role))
                            claims.Add(new Claim(ClaimTypes.Role, role));
                    }
                }
                else if (rolesEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var role = rolesEl.GetString();
                    if (!string.IsNullOrEmpty(role))
                        claims.Add(new Claim(ClaimTypes.Role, role));
                }
            }

            if (root.TryGetProperty(upnClaimType, out var upnEl)
                && upnEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var upn = upnEl.GetString();
                if (!string.IsNullOrEmpty(upn))
                    claims.Add(new Claim(CallerIdentityAccessor.AdminUpnClaim, upn));
            }

            if (root.TryGetProperty("sub", out var subEl)
                && subEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var sub = subEl.GetString();
                if (!string.IsNullOrEmpty(sub))
                    claims.Add(new Claim(ClaimTypes.NameIdentifier, sub));
            }
        }
        catch
        {
        }
        return claims;
    }

    private static string Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
