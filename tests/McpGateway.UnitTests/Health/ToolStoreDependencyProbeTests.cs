using FluentAssertions;
using McpGateway.Core.Health;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;

namespace McpGateway.UnitTests.Health;

public class ToolStoreDependencyProbeTests
{
    [Fact]
    public async Task ProbeAsync_EmptyStore_Fails()
    {
        var store = new InMemoryToolStore();
        var probe = new ToolStoreDependencyProbe(store);

        var result = await probe.ProbeAsync();

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("no server");
    }

    [Fact]
    public async Task ProbeAsync_WithServer_Succeeds()
    {
        var store = new InMemoryToolStore();
        store.AddServer(new McpServerDefinition
        {
            Name = "invoice-api",
            DisplayName = "Invoice",
            BaseUrl = "https://invoice.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}"
        });
        var probe = new ToolStoreDependencyProbe(store);

        var result = await probe.ProbeAsync();

        result.Ok.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Name_IsToolStore()
    {
        var store = new InMemoryToolStore();
        var probe = new ToolStoreDependencyProbe(store);

        probe.Name.Should().Be("tool_store");
    }
}
