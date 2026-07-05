using FluentAssertions;
using McpGateway.Core.McpUpstream;
using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.UnitTests.Proxy;

public class McpUpstreamInvocationStrategyTests
{
    [Fact]
    public async Task Invoke_forwards_call_to_upstream_client()
    {
        using var cts = new CancellationTokenSource();
        var expectedArguments = new Dictionary<string, object?> { ["msg"] = "hi" };
        var fakeClient = new CapturingUpstreamClient();

        var strategy = new McpUpstreamInvocationStrategy(fakeClient);
        var server = new McpServerDefinition
        {
            Name = "upstream",
            DisplayName = "upstream",
            BaseUrl = "https://upstream.example.com/mcp",
            SpecHash = "hash",
            AuthStrategy = "obo",
            AuthConfig = "{}",
            SourceType = SourceType.McpUpstream
        };
        var tool = new ToolDefinition
        {
            ToolName = "echo",
            Description = "echo",
            HttpMethod = null,
            HttpPath = null
        };

        var result = await strategy.InvokeAsync(server, tool, expectedArguments, cts.Token);

        strategy.SourceType.Should().Be(SourceType.McpUpstream);
        fakeClient.CapturedEndpoint.Should().Be(server.BaseUrl);
        fakeClient.CapturedToolName.Should().Be(tool.ToolName);
        fakeClient.CapturedArguments.Should().BeEquivalentTo(expectedArguments);
        fakeClient.CapturedCancellationToken.Should().Be(cts.Token);
        result.IsError.Should().BeFalse();
        result.Content.Should().ContainSingle()
            .Which.Text.Should().Be("echo:hi");
    }

    private sealed class CapturingUpstreamClient : IMcpUpstreamClient
    {
        public string? CapturedEndpoint { get; private set; }
        public string? CapturedToolName { get; private set; }
        public IReadOnlyDictionary<string, object?>? CapturedArguments { get; private set; }
        public CancellationToken CapturedCancellationToken { get; private set; }

        public Task<IReadOnlyList<UpstreamTool>> ListToolsAsync(string endpoint, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UpstreamTool>>(new List<UpstreamTool>());

        public Task<ToolCallResult> CallToolAsync(
            string endpoint,
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken ct = default)
        {
            CapturedEndpoint = endpoint;
            CapturedToolName = toolName;
            CapturedArguments = arguments;
            CapturedCancellationToken = ct;
            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ToolCallContent> { new() { Type = "text", Text = $"{toolName}:{arguments["msg"]}" } }
            });
        }
    }
}
