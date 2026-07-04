using System.Text.Json;
using McpGateway.BddTests.Support;
using WireMock.Server;

namespace McpGateway.BddTests.Steps;

public sealed class TestContext
{
    public BddWebApplicationFactory? Factory { get; set; }
    public EntraIdMock? EntraId { get; set; }
    public WireMockServer? UpstreamApi { get; set; }
    public JwtTestSigner? Signer { get; set; }
    public string? ServerName { get; set; }
    public string? SpecContent { get; set; }
    public string? CurrentToken { get; set; }
    public string? LastResponseBody { get; set; }
    public int? LastResponseStatus { get; set; }
    public JsonDocument? LastJsonResponse { get; set; }
    public string? CapturedAuthorizationHeader { get; set; }

    public void Reset()
    {
        ServerName = null;
        SpecContent = null;
        CurrentToken = null;
        LastResponseBody = null;
        LastResponseStatus = null;
        LastJsonResponse?.Dispose();
        LastJsonResponse = null;
        CapturedAuthorizationHeader = null;
    }
}
