using FluentAssertions;
using McpGateway.Core.Health;
using Microsoft.Extensions.Configuration;

namespace McpGateway.UnitTests.Health;

public class StorageQueueDependencyProbeTests
{
    [Fact]
    public async Task ProbeAsync_InvalidConnection_Fails()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StorageQueue"] = "DefaultEndpointsProtocol=https;AccountName=does-not-exist;AccountKey=YWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWE=;EndpointSuffix=core.windows.net"
            })
            .Build();
        var probe = new StorageQueueDependencyProbe(config, "StorageQueue");

        var result = await probe.ProbeAsync();

        result.Ok.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProbeAsync_MissingConnectionString_Fails()
    {
        var config = new ConfigurationBuilder().Build();
        var probe = new StorageQueueDependencyProbe(config, "StorageQueue");

        var result = await probe.ProbeAsync();

        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("StorageQueue");
    }

    [Fact]
    public void Name_IsStorageQueue()
    {
        var config = new ConfigurationBuilder().Build();
        var probe = new StorageQueueDependencyProbe(config, "StorageQueue");

        probe.Name.Should().Be("storage_queue");
    }
}
