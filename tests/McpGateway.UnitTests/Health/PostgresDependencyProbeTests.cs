using FluentAssertions;
using McpGateway.Core.Health;
using Microsoft.Extensions.Configuration;

namespace McpGateway.UnitTests.Health;

public class PostgresDependencyProbeTests
{
    [Fact]
    public async Task ProbeAsync_InvalidConnection_Fails()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = "Host=127.0.0.1;Port=1;Database=does_not_exist;Username=u;Password=p;Timeout=2;Command Timeout=2"
            })
            .Build();
        var probe = new PostgresDependencyProbe(config, "PostgreSql");

        var result = await probe.ProbeAsync();

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProbeAsync_MissingConnectionString_Fails()
    {
        var config = new ConfigurationBuilder().Build();
        var probe = new PostgresDependencyProbe(config, "PostgreSql");

        var result = await probe.ProbeAsync();

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("PostgreSql");
    }

    [Fact]
    public void Name_IsPostgres()
    {
        var config = new ConfigurationBuilder().Build();
        var probe = new PostgresDependencyProbe(config, "PostgreSql");

        probe.Name.Should().Be("postgres");
    }
}
