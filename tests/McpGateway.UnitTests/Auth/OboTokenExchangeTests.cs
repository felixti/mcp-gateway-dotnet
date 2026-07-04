using System.Net;
using System.Text.Json;
using FluentAssertions;
using McpGateway.Core.Auth;

namespace McpGateway.UnitTests.Auth;

public class OboTokenExchangeTests
{
    [Fact]
    public async Task ExchangeAsync_ReturnsAccessToken()
    {
        var handler = new TestMessageHandler(req =>
        {
            var content = JsonSerializer.Serialize(new { access_token = "exchanged-token", expires_in = 3600 });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            };
        });

        var exchange = new OboTokenExchange(new HttpClient(handler), new OboTokenExchangeOptions
        {
            TokenEndpoint = "https://login.microsoftonline.com/test/oauth2/v2.0/token",
            ClientId = "client-id",
            ClientSecret = "secret"
        });

        var token = await exchange.ExchangeAsync("caller-token", "api://target/.default");

        token.Should().Be("exchanged-token");
    }

    private class TestMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public TestMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
