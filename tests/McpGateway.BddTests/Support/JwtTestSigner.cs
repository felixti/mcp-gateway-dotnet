using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace McpGateway.BddTests.Support;

public sealed class JwtTestSigner
{
    public string KeyId { get; }
    public string Issuer { get; }
    public string Audience { get; }
    public RSA PrivateKey { get; }
    public string JwksJson { get; }

    public JwtTestSigner(string keyId, string issuer, string audience)
    {
        KeyId = keyId;
        Issuer = issuer;
        Audience = audience;
        PrivateKey = RSA.Create(2048);
        JwksJson = BuildJwks();
    }

    public string SignToken(string subject, IEnumerable<Claim>? extraClaims = null)
    {
        var key = new RsaSecurityKey(PrivateKey) { KeyId = KeyId };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new("oid", subject),
            new("preferred_username", $"{subject}@example.com")
        };
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string BuildJwks()
    {
        var parameters = PrivateKey.ExportParameters(includePrivateParameters: false);
        var jwk = new
        {
            kty = "RSA",
            use = "sig",
            kid = KeyId,
            alg = "RS256",
            n = Base64UrlEncoder.Encode(parameters.Modulus!),
            e = Base64UrlEncoder.Encode(parameters.Exponent!)
        };
        return "{\"keys\":[" + System.Text.Json.JsonSerializer.Serialize(jwk) + "]}";
    }
}
