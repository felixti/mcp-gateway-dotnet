using FluentAssertions;
using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.UnitTests.Proxy;

public class HttpInvocationStrategyTests
{
    [Fact]
    public async Task Invoke_builds_request_and_wraps_response()
    {
        var capturedRequest = (HttpRequestMessage?)null;
        var stubHandler = new TestHttpMessageHandler((request, ct) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("hello")
            });
        });

        var requestBuilder = new HttpRequestBuilder();
        var responseWrapper = new ResponseWrapper();
        var httpClient = new HttpClient(stubHandler)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };

        var strategy = new HttpInvocationStrategy(requestBuilder, responseWrapper, httpClient);
        var server = new McpServerDefinition
        {
            Name = "api",
            DisplayName = "api",
            BaseUrl = "https://api.example.com",
            SpecHash = "hash",
            AuthStrategy = "obo",
            AuthConfig = "{}"
        };
        var tool = new ToolDefinition
        {
            ToolName = "greet",
            Description = "greet",
            HttpMethod = "GET",
            HttpPath = "/hello"
        };

        var result = await strategy.InvokeAsync(server, tool, new Dictionary<string, object?>(), CancellationToken.None);

        strategy.SourceType.Should().Be(SourceType.OpenApi);
        result.IsError.Should().BeFalse();
        result.Content.Should().ContainSingle()
            .Which.Text.Should().Be("hello");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Method.Should().Be(HttpMethod.Get);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public TestHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _sendAsync(request, cancellationToken);
        }
    }
}
