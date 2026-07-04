using FluentAssertions;
using McpGateway.Core.Health;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using McpGateway.IntegrationTests.Health.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace McpGateway.IntegrationTests.Health;

[Collection("Health")]
public class DependencyHealthCheckerIntegrationTests
{
    private readonly PostgreSqlHealthFixture _pg;
    private readonly AzuriteHealthFixture _azurite;

    public DependencyHealthCheckerIntegrationTests(
        PostgreSqlHealthFixture pg,
        AzuriteHealthFixture azurite)
    {
        _pg = pg;
        _azurite = azurite;
    }

    [Fact]
    public async Task RunCheckAsync_AllProbesOk_MarksReady()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = _pg.ConnectionString,
                ["ConnectionStrings:StorageQueue"] = _azurite.ConnectionString
            })
            .Build();

        var state = new ReadinessState();
        var store = new InMemoryToolStore();
        store.AddServer(new McpServerDefinition
        {
            Name = "s",
            DisplayName = "S",
            BaseUrl = "https://s.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}"
        });

        var probes = new IDependencyProbe[]
        {
            new PostgresDependencyProbe(config, "PostgreSql"),
            new StorageQueueDependencyProbe(config, "StorageQueue"),
            new ToolStoreDependencyProbe(store)
        };

        var checker = new DependencyHealthChecker(
            state,
            probes,
            Options.Create(new HealthOptions()),
            NullLogger<DependencyHealthChecker>.Instance);

        await checker.RunCheckAsync();

        state.Current.IsReady.Should().BeTrue();
        state.Current.PostgresOk.Should().BeTrue();
        state.Current.StorageQueueOk.Should().BeTrue();
        state.Current.ToolStoreOk.Should().BeTrue();
        state.Current.LastCheckedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunCheckAsync_PostgresDown_StorageQueueDown_StillReportsPerProbe()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=1;Command Timeout=1",
                ["ConnectionStrings:StorageQueue"] = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;QueueEndpoint=http://127.0.0.1:1/devstoreaccount1;"
            })
            .Build();

        var state = new ReadinessState();
        var store = new InMemoryToolStore();
        store.AddServer(new McpServerDefinition
        {
            Name = "s",
            DisplayName = "S",
            BaseUrl = "https://s.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}"
        });

        var probes = new IDependencyProbe[]
        {
            new PostgresDependencyProbe(config, "PostgreSql"),
            new StorageQueueDependencyProbe(config, "StorageQueue"),
            new ToolStoreDependencyProbe(store)
        };

        var checker = new DependencyHealthChecker(
            state,
            probes,
            Options.Create(new HealthOptions()),
            NullLogger<DependencyHealthChecker>.Instance);

        await checker.RunCheckAsync();

        state.Current.IsReady.Should().BeFalse();
        state.Current.PostgresOk.Should().BeFalse();
        state.Current.StorageQueueOk.Should().BeFalse();
        state.Current.ToolStoreOk.Should().BeTrue();
        state.Current.PostgresError.Should().NotBeNullOrEmpty();
        state.Current.StorageQueueError.Should().NotBeNullOrEmpty();
    }
}
