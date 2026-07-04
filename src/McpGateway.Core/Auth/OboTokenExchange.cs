using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace McpGateway.Core.Auth;

public class OboTokenExchange : IOboTokenExchange
{
    private readonly HttpClient _httpClient;
    private readonly OboTokenExchangeOptions _options;

    public OboTokenExchange(HttpClient httpClient, OboTokenExchangeOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<string> ExchangeAsync(string callerToken, string resource, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["assertion"] = callerToken,
                ["scope"] = resource,
                ["requested_token_use"] = "on_behalf_of"
            })
        };

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<OboTokenResponse>(ct);
        return tokenResponse?.AccessToken
            ?? throw new InvalidOperationException("OBO response did not contain access_token.");
    }
}

public class OboTokenExchangeOptions
{
    public string TokenEndpoint { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    public string ClientSecret { get; set; } = null!;
}

public class OboTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = null!;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
