using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.Audit;
using McpGateway.Core.Health;
using McpGateway.Core.Proxy;
using McpGateway.Core.Proxy.Exceptions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using NSubstitute;

namespace McpGateway.UnitTests.Proxy;

public class ToolCallHandlerTests
{
    private readonly InMemoryToolStore _store = new();
    private readonly IAuditEmitter _auditEmitter = Substitute.For<IAuditEmitter>();
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ToolCallContextAccessor _contextAccessor = new();
    private readonly IInFlightCallTracker _inFlightCallTracker = new InFlightCallTracker();

    [Fact]
    public async Task HandleAsync_UnknownServer_Throws()
    {
        var handler = CreateHandler();
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.HandleAsync("unknown", "tool", new Dictionary<string, object?>(), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownTool_Throws()
    {
        _store.AddServer(CreateServer("api", []));
        var handler = CreateHandler();

        await Assert.ThrowsAsync<ToolNotFoundException>(
            () => handler.HandleAsync("api", "missing", new Dictionary<string, object?>(), CancellationToken.None));
    }

    private ToolCallHandler CreateHandler()
    {
        var httpClient = new HttpClient(new TestHttpMessageHandler());
        return new ToolCallHandler(
            _store,
            new HttpRequestBuilder(),
            new ResponseWrapper(),
            httpClient,
            _auditEmitter,
            _timeProvider,
            _contextAccessor,
            _inFlightCallTracker);
    }

    private static McpServerDefinition CreateServer(string name, List<ToolDefinition> tools) => new()
    {
        Name = name,
        DisplayName = name,
        BaseUrl = "https://api.example.com",
        SpecHash = "hash",
        AuthStrategy = "obo",
        AuthConfig = "{}",
        Tools = tools
    };

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
        }
    }
}
