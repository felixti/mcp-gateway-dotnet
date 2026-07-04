using Azure.Storage.Queues;
using Testcontainers.Azurite;

namespace McpGateway.IntegrationTests.Audit;

public sealed class AzuriteFixture : IAsyncLifetime
{
    public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.34.0";
    public const string QueuePort = "10001";

    private readonly AzuriteContainer _container;

    public AzuriteFixture()
    {
        _container = new AzuriteBuilder(AzuriteImage)
            .WithPortBinding(QueuePort, true)
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public QueueClient CreateQueueClient(string queueName)
    {
        return new QueueClient(ConnectionString, queueName);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
