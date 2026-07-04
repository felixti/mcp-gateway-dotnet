using FluentAssertions;
using McpGateway.Core.Health;
using McpGateway.Core.ToolStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace McpGateway.UnitTests.Health;

public class CoreHealthServiceExtensionsTests
{
    [Fact]
    public void AddMcpHealth_RegistersAllHealthServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Health:DependencyCheckIntervalSeconds"] = "5"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IToolStore, InMemoryToolStore>();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);

        services.AddMcpHealth(config);

        using var provider = services.BuildServiceProvider();

        provider.GetService<IReadinessState>().Should().NotBeNull();
        provider.GetService<IInFlightCallTracker>().Should().NotBeNull();
        provider.GetServices<IDependencyProbe>().Should().NotBeEmpty();
    }

    [Fact]
    public void AddMcpHealth_BindsHealthOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Health:DependencyCheckIntervalSeconds"] = "7",
                ["Health:ShutdownDrainTimeoutSeconds"] = "15"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IToolStore, InMemoryToolStore>();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);

        services.AddMcpHealth(config);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthOptions>>().Value;
        options.DependencyCheckIntervalSeconds.Should().Be(7);
        options.ShutdownDrainTimeoutSeconds.Should().Be(15);
    }
}
