using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.Audit;
using McpGateway.Core.Auth;
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
        _store.AddServer(CreateServer("api", SourceType.OpenApi, []));
        var handler = CreateHandler();

        await Assert.ThrowsAsync<ToolNotFoundException>(
            () => handler.HandleAsync("api", "missing", new Dictionary<string, object?>(), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_Dispatches_To_Strategy_Matching_SourceType()
    {
        var server = CreateServer("api", SourceType.McpUpstream, [new ToolDefinition { ToolName = "greet" }]);
        _store.AddServer(server);

        var expectedResult = new ToolCallResult
        {
            Content = [new ToolCallContent { Text = "fake" }],
            HttpStatus = 200
        };
        var fakeStrategy = new FakeStrategy(SourceType.McpUpstream, expectedResult);
        var handler = CreateHandler(fakeStrategy);

        var args = new Dictionary<string, object?> { ["name"] = "world" };
        var result = await handler.HandleAsync("api", "greet", args, CancellationToken.None);

        fakeStrategy.ReceivedServer.Should().BeSameAs(server);
        fakeStrategy.ReceivedTool!.ToolName.Should().Be("greet");
        fakeStrategy.ReceivedArguments.Should().BeSameAs(args);
        fakeStrategy.ReceivedCancellationToken.Should().Be(CancellationToken.None);
        result.Should().BeSameAs(expectedResult);
    }

    [Fact]
    public async Task HandleAsync_NoStrategyForSourceType_Throws()
    {
        _store.AddServer(CreateServer("api", SourceType.McpUpstream, [new ToolDefinition { ToolName = "greet" }]));
        var handler = CreateHandler(); // only registers strategies passed explicitly; none here

        var act = () => handler.HandleAsync("api", "greet", new Dictionary<string, object?>(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No tool invocation strategy*")
            .WithMessage($"*{SourceType.McpUpstream.ToCanonicalString()}*");
    }

    [Fact]
    public async Task HandleAsync_Uses_ResultHttpStatus_For_Audit()
    {
        var server = CreateServer("api", SourceType.McpUpstream, [new ToolDefinition { ToolName = "greet" }]);
        _store.AddServer(server);

        var expectedResult = new ToolCallResult
        {
            Content = [new ToolCallContent { Text = "fake" }],
            HttpStatus = 418
        };
        var fakeStrategy = new FakeStrategy(SourceType.McpUpstream, expectedResult);
        _contextAccessor.Current = new ToolCallContext
        {
            Caller = new CallerIdentity { Id = "caller-1", Name = "Tester", AuthMethod = GatewayAuthMethod.GatewayApiKey },
            ServerName = "api",
            ToolName = "greet"
        };
        var handler = CreateHandler(fakeStrategy);

        await handler.HandleAsync("api", "greet", new Dictionary<string, object?>(), CancellationToken.None);

        await _auditEmitter.Received(1).EmitAsync(
            Arg.Is<AuditEvent>(e => e.HttpStatus == 418 && e.IsError == expectedResult.IsError),
            Arg.Any<CancellationToken>());
    }

    private ToolCallHandler CreateHandler(params IToolInvocationStrategy[] strategies)
    {
        return new ToolCallHandler(
            _store,
            strategies,
            _auditEmitter,
            _timeProvider,
            _contextAccessor,
            _inFlightCallTracker);
    }

    private static McpServerDefinition CreateServer(string name, SourceType sourceType, List<ToolDefinition> tools) => new()
    {
        Name = name,
        DisplayName = name,
        BaseUrl = "https://api.example.com",
        SpecHash = "hash",
        AuthStrategy = "obo",
        AuthConfig = "{}",
        SourceType = sourceType,
        Tools = tools
    };

    private class FakeStrategy : IToolInvocationStrategy
    {
        private readonly SourceType _sourceType;
        private readonly ToolCallResult _result;

        public FakeStrategy(SourceType sourceType, ToolCallResult result)
        {
            _sourceType = sourceType;
            _result = result;
        }

        public SourceType SourceType => _sourceType;

        public McpServerDefinition? ReceivedServer { get; private set; }
        public ToolDefinition? ReceivedTool { get; private set; }
        public IReadOnlyDictionary<string, object?>? ReceivedArguments { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<ToolCallResult> InvokeAsync(
            McpServerDefinition server,
            ToolDefinition tool,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken ct = default)
        {
            ReceivedServer = server;
            ReceivedTool = tool;
            ReceivedArguments = arguments;
            ReceivedCancellationToken = ct;
            return Task.FromResult(_result);
        }
    }
}
