namespace McpGateway.Management.Auth;

public interface ICallerIdentityAccessor
{
    /// <summary>
    /// Returns the admin's identity (Entra ID UPN for Entra, key prefix + id for API key admin).
    /// Throws InvalidOperationException if no admin identity is present.
    /// </summary>
    string GetAdminUpn();

    /// <summary>
    /// Returns true if the current request has a verified admin identity.
    /// </summary>
    bool IsAdmin { get; }
}
