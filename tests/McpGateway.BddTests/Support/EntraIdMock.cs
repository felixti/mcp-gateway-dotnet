using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace McpGateway.BddTests.Support;

public sealed class EntraIdMock : IAsyncDisposable
{
    public WireMockServer Server { get; }
    public string Authority { get; }
    public string TokenEndpoint { get; }
    public string TenantId { get; }
    public string ClientId { get; }

    public EntraIdMock(string tenantId, string clientId)
    {
        TenantId = tenantId;
        ClientId = clientId;
        Server = WireMockServer.Start();
        Authority = Server.Url!;
        TokenEndpoint = $"{Authority}/{tenantId}/oauth2/v2.0/token";

        Server
            .Given(Request.Create().WithPath($"/{tenantId}/oauth2/v2.0/token").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new
                {
                    access_token = "obo-m2m-token",
                    expires_in = 3600,
                    token_type = "Bearer"
                }));
    }

    public string Value => $"{Authority.TrimEnd('/')}/{TenantId}/v2.0";

    public async ValueTask DisposeAsync()
    {
        Server.Stop();
        Server.Dispose();
        await Task.CompletedTask;
    }
}
