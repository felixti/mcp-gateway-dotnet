using FluentAssertions;
using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;

namespace McpGateway.UnitTests.Proxy;

public class MetaToolsHandlerTests
{
    private readonly InMemoryToolStore _store = new();

    public MetaToolsHandlerTests()
    {
        _store.AddServer(new McpServerDefinition
        {
            Name = "large-api",
            DisplayName = "Large API",
            BaseUrl = "https://api.example.com",
            SpecHash = "hash",
            AuthStrategy = "obo",
            ToolMode = ToolMode.Dynamic,
            Tools =
            [
                new ToolDefinition
                {
                    ToolName = "get_users",
                    Description = "Get users",
                    HttpMethod = "GET",
                    HttpPath = "/users",
                    InputSchema = "{}"
                }
            ]
        });
    }

    [Fact]
    public async Task ListApiEndpoints_ReturnsToolList()
    {
        var handler = new MetaToolsHandler(_store);
        var result = await handler.HandleAsync("large-api", "list_api_endpoints", new Dictionary<string, object?>(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("get_users");
    }

    [Fact]
    public async Task GetApiEndpointSchema_ReturnsSchema()
    {
        var handler = new MetaToolsHandler(_store);
        var result = await handler.HandleAsync("large-api", "get_api_endpoint_schema",
            new Dictionary<string, object?> { ["tool_name"] = "get_users" }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("\"HttpMethod\":\"GET\"");
        result.Content[0].Text.Should().Contain("\"/users\"");
    }
}
