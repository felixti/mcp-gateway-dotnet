using FluentAssertions;
using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.UnitTests.Proxy;

public class HttpRequestBuilderTests
{
    private readonly HttpRequestBuilder _builder = new();

    [Fact]
    public void Build_PathParamsSubstituted()
    {
        var tool = CreateTool("GET", "/users/{id}");
        var args = new Dictionary<string, object?> { ["id"] = "123" };

        var request = _builder.Build("https://api.example.com", tool, args);

        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.ToString().Should().Be("https://api.example.com/users/123");
    }

    [Fact]
    public void Build_QueryParamsAdded()
    {
        var tool = CreateTool("GET", "/users");
        var args = new Dictionary<string, object?> { ["limit"] = 10, ["offset"] = 20 };

        var request = _builder.Build("https://api.example.com", tool, args);

        var uri = request.RequestUri!.ToString();
        uri.Should().Contain("limit=10");
        uri.Should().Contain("offset=20");
    }

    [Fact]
    public async Task Build_BodyParamSerialized()
    {
        var tool = CreateTool("POST", "/users");
        var args = new Dictionary<string, object?> { ["body"] = new { name = "Alice" } };

        var request = _builder.Build("https://api.example.com", tool, args);

        request.Content.Should().NotBeNull();
        var body = await request.Content!.ReadAsStringAsync();
        body.Should().Contain("Alice");
    }

    [Fact]
    public void Build_throws_when_tool_has_no_http_coordinates()
    {
        var tool = new ToolDefinition
        {
            ToolName = "non_http_tool",
            Description = "Not backed by HTTP",
            InputSchema = "{}"
        };
        var args = new Dictionary<string, object?>();

        var act = () => _builder.Build("https://api.example.com", tool, args);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Tool 'non_http_tool' has no HTTP coordinates; it is not an OpenAPI-backed tool.");
    }

    private static ToolDefinition CreateTool(string method, string path) => new()
    {
        ToolName = "test_tool",
        Description = "Test",
        HttpMethod = method,
        HttpPath = path,
        InputSchema = "{}"
    };
}
