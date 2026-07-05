using System.Text.Json;
using System.Text.Json.Nodes;
using McpGateway.Core.McpUpstream;
using McpGateway.Core.Proxy;
using ModelContextProtocol.Protocol;

namespace McpGateway.UnitTests.McpUpstream;

public class SdkMcpUpstreamClientTests
{
    [Fact]
    public async Task ListToolsAsync_returns_empty_list_from_fake()
    {
        IMcpUpstreamClient client = new FakeUpstreamClient();
        var tools = await client.ListToolsAsync("https://upstream.example.com/mcp", default);
        Assert.Empty(tools);
    }

    [Fact]
    public async Task CallToolAsync_returns_text_content_from_upstream()
    {
        IMcpUpstreamClient client = new FakeUpstreamClient();
        var result = await client.CallToolAsync(
            "https://upstream.example.com/mcp", "echo",
            new Dictionary<string, object?> { ["msg"] = "hi" }, default);

        Assert.False(result.IsError);
        Assert.Equal("echo:hi", result.Content[0].Text);
    }

    [Fact]
    public async Task Sdk_ListToolsAsync_maps_SDK_tools_to_UpstreamTool()
    {
        var schema = JsonSerializer.SerializeToElement(new JsonObject { ["type"] = "object" });
        var session = new FakeMcpClientSession(
            tools: new List<Tool>
            {
                new()
                {
                    Name = "echo",
                    Description = "echo tool",
                    InputSchema = schema
                }
            });

        var client = new SdkMcpUpstreamClient((_, _) => Task.FromResult<IMcpClientSession>(session));
        var tools = await client.ListToolsAsync("https://upstream.example.com/mcp", default);

        var tool = Assert.Single(tools);
        Assert.Equal("echo", tool.Name);
        Assert.Equal("echo tool", tool.Description);
        Assert.NotNull(tool.InputSchema);
        Assert.Equal("object", tool.InputSchema!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sdk_CallToolAsync_maps_SDK_result_to_ToolCallResult()
    {
        var session = new FakeMcpClientSession(
            result: new CallToolResult
            {
                Content = new List<ContentBlock> { new TextContentBlock { Text = "hello" } },
                IsError = false
            });

        var client = new SdkMcpUpstreamClient((_, _) => Task.FromResult<IMcpClientSession>(session));
        var result = await client.CallToolAsync(
            "https://upstream.example.com/mcp", "echo",
            new Dictionary<string, object?> { ["msg"] = "hi" }, default);

        Assert.False(result.IsError);
        var content = Assert.Single(result.Content);
        Assert.Equal("text", content.Type);
        Assert.Equal("hello", content.Text);
    }

    [Fact]
    public async Task Sdk_CallToolAsync_preserves_IsError_and_maps_unknown_content_as_json()
    {
        var session = new FakeMcpClientSession(
            result: new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new ImageContentBlock
                    {
                        Data = Convert.FromBase64String("YXJ0aWZhY3Q="),
                        MimeType = "image/png"
                    }
                },
                IsError = true
            });

        var client = new SdkMcpUpstreamClient((_, _) => Task.FromResult<IMcpClientSession>(session));
        var result = await client.CallToolAsync(
            "https://upstream.example.com/mcp", "echo",
            new Dictionary<string, object?>(), default);

        Assert.True(result.IsError);
        var content = Assert.Single(result.Content);
        Assert.Equal("image", content.Type);
        Assert.Contains("image", content.Text);
    }

    private sealed class FakeUpstreamClient : IMcpUpstreamClient
    {
        public Task<IReadOnlyList<UpstreamTool>> ListToolsAsync(string endpoint, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UpstreamTool>>(new List<UpstreamTool>());

        public Task<ToolCallResult> CallToolAsync(string endpoint, string toolName,
            IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
            => Task.FromResult(new ToolCallResult
            {
                Content = new List<ToolCallContent> { new() { Type = "text", Text = $"echo:{arguments["msg"]}" } }
            });
    }

    private sealed class FakeMcpClientSession : IMcpClientSession
    {
        private readonly IReadOnlyList<Tool> _tools;
        private readonly CallToolResult? _result;

        public FakeMcpClientSession(
            IReadOnlyList<Tool>? tools = null,
            CallToolResult? result = null)
        {
            _tools = tools ?? [];
            _result = result;
        }

        public Task<IReadOnlyList<Tool>> ListToolsAsync(CancellationToken ct)
            => Task.FromResult(_tools);

        public Task<CallToolResult> CallToolAsync(
            string toolName,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken ct)
        {
            if (_result is null)
                throw new InvalidOperationException("No result configured for this fake session.");
            return Task.FromResult(_result);
        }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
}
