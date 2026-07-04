namespace McpGateway.Api.Auth;

public static class AuthResolver
{
    public static string ResolveAuthScheme(HttpContext context)
    {
        if (context.Request.Headers.ContainsKey(ApiKeyAuthHandler.HeaderName))
        {
            return ApiKeyAuthHandler.SchemeName;
        }

        if (context.Request.Headers.Authorization.ToString().StartsWith("Bearer "))
        {
            return JwtAuthHandler.SchemeName;
        }

        return string.Empty;
    }
}
