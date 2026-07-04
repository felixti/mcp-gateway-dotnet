using Microsoft.Extensions.Configuration;
using Npgsql;

namespace McpGateway.Core.Health;

public class PostgresDependencyProbe : IDependencyProbe
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionName;

    public PostgresDependencyProbe(IConfiguration configuration, string connectionName)
    {
        _configuration = configuration;
        _connectionName = connectionName;
    }

    public string Name => "postgres";

    public async Task<ProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        var connectionString = _configuration.GetConnectionString(_connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return ProbeResult.Failure($"Connection string '{_connectionName}' is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await connection.OpenAsync(cts.Token);

            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var result = await command.ExecuteScalarAsync(cts.Token);
            return result is 1 or (long)1 or (int)1
                ? ProbeResult.Success()
                : ProbeResult.Failure("PostgreSQL SELECT 1 returned unexpected value.");
        }
        catch (OperationCanceledException)
        {
            return ProbeResult.Failure("PostgreSQL probe timed out.");
        }
        catch (NpgsqlException ex)
        {
            return ProbeResult.Failure($"PostgreSQL probe failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ProbeResult.Failure($"PostgreSQL probe failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
