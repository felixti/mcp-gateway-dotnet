using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace McpGateway.Management.Auth;

public class CallerIdentityAccessor : ICallerIdentityAccessor
{
    public const string AdminRole = "mcp-gateway-admin";
    public const string AdminUpnClaim = "admin_upn";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CallerIdentityAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAdmin
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null || user.Identity?.IsAuthenticated != true) return false;
            return user.IsInRole(AdminRole) || user.HasClaim(c => c.Type == AdminUpnClaim);
        }
    }

    public string GetAdminUpn()
    {
        var user = _httpContextAccessor.HttpContext?.User
            ?? throw new InvalidOperationException("No HTTP context available.");

        var upn = user.FindFirstValue(AdminUpnClaim)
            ?? user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(upn))
            throw new InvalidOperationException("Admin identity missing from token claims.");

        return upn;
    }
}
