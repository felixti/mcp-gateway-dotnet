using McpGateway.Core.McpUpstream;
using McpGateway.Core.Proxy;

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
}
