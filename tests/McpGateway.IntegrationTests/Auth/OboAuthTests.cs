using System.Net;
using FluentAssertions;
using McpGateway.Core.Auth;

namespace McpGateway.IntegrationTests.Auth;

public class OboAuthTests
{
    [Fact]
    public async Task OboTokenExchange_HitsMockEndpoint()
    {
        var handler = new TestMessageHandler(req =>
        {
            req.RequestUri!.ToString().Should().Contain("oauth2");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"access_token\":\"obo-token\",\"expires_in\":3600}")
            };
        });

        var exchange = new OboTokenExchange(new HttpClient(handler), new OboTokenExchangeOptions
        {
            TokenEndpoint = "https://login.microsoftonline.com/test/oauth2/v2.0/token",
            ClientId = "client",
            ClientSecret = "secret"
        });

        var token = await exchange.ExchangeAsync("caller-jwt", "api://target/.default");
        token.Should().Be("obo-token");
    }

    private class TestMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public TestMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_responder(request));
    }
}
