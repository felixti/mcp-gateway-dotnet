using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;

namespace McpGateway.UnitTests.ToolStore;

public class InMemoryToolStoreTests
{
    private readonly InMemoryToolStore _store = new();

    private static McpServerDefinition CreateServer(string name) => new()
    {
        Name = name,
        DisplayName = name,
        BaseUrl = $"https://{name}.example.com",
        SpecHash = "hash",
        AuthStrategy = "obo",
        AuthConfig = "{}"
    };

    [Fact]
    public void AddServer_ThenGet_ReturnsServer()
    {
        var server = CreateServer("invoice-api");

        _store.AddServer(server);
        var retrieved = _store.GetServer("invoice-api");

        retrieved.Should().BeSameAs(server);
    }

    [Fact]
    public void GetServer_Unknown_ReturnsNull()
    {
        var retrieved = _store.GetServer("unknown");
        retrieved.Should().BeNull();
    }

    [Fact]
    public void UpdateServer_ReplacesServer()
    {
        var first = CreateServer("api");
        var second = CreateServer("api");
        second.BaseUrl = "https://updated.example.com";

        _store.AddServer(first);
        _store.UpdateServer(second);

        _store.GetServer("api")!.BaseUrl.Should().Be("https://updated.example.com");
    }

    [Fact]
    public void RemoveServer_Existing_ReturnsTrue()
    {
        _store.AddServer(CreateServer("api"));
        _store.RemoveServer("api").Should().BeTrue();
        _store.Contains("api").Should().BeFalse();
    }

    [Fact]
    public void RemoveServer_Unknown_ReturnsFalse()
    {
        _store.RemoveServer("unknown").Should().BeFalse();
    }

    [Fact]
    public void GetAllServers_ReturnsAll()
    {
        _store.AddServer(CreateServer("a"));
        _store.AddServer(CreateServer("b"));

        _store.GetAllServers().Should().HaveCount(2);
    }

    [Fact]
    public void AddServer_Null_Throws()
    {
        Action act = () => _store.AddServer(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddServer_EmptyName_Throws()
    {
        Action act = () => _store.AddServer(new McpServerDefinition { Name = "" });
        act.Should().Throw<ArgumentException>();
    }
}
