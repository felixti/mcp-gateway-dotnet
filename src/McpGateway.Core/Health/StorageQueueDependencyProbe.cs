using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;

namespace McpGateway.Core.Health;

public class StorageQueueDependencyProbe : IDependencyProbe
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionName;

    public StorageQueueDependencyProbe(IConfiguration configuration, string connectionName)
    {
        _configuration = configuration;
        _connectionName = connectionName;
    }

    public string Name => "storage_queue";

    public async Task<ProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        var connectionString = _configuration.GetConnectionString(_connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return ProbeResult.Failure($"Connection string '{_connectionName}' is not configured.");
        }

        try
        {
            var serviceClient = new QueueServiceClient(connectionString);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await serviceClient.GetPropertiesAsync(cts.Token);
            return ProbeResult.Success();
        }
        catch (OperationCanceledException)
        {
            return ProbeResult.Failure("Storage queue probe timed out.");
        }
        catch (Exception ex)
        {
            return ProbeResult.Failure($"Storage queue probe failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
