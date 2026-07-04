using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpGateway.Core.Health;

public class DependencyHealthChecker : BackgroundService
{
    private readonly IReadinessState _state;
    private readonly IReadOnlyList<IDependencyProbe> _probes;
    private readonly HealthOptions _options;
    private readonly ILogger<DependencyHealthChecker> _logger;

    public DependencyHealthChecker(
        IReadinessState state,
        IEnumerable<IDependencyProbe> probes,
        IOptions<HealthOptions> options,
        ILogger<DependencyHealthChecker> logger)
    {
        _state = state;
        _probes = probes.ToList();
        _options = options.Value;
        _logger = logger;
    }

    public virtual async Task RunCheckAsync(CancellationToken ct = default)
    {
        var results = new Dictionary<string, ProbeResult>(StringComparer.Ordinal);
        foreach (var probe in _probes)
        {
            try
            {
                results[probe.Name] = await probe.ProbeAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Probe {Probe} threw unexpectedly.", probe.Name);
                results[probe.Name] = ProbeResult.Failure(ex.GetType().Name + ": " + ex.Message);
            }
        }

        var snapshot = new ReadinessSnapshot(
            IsReady: results.Values.All(r => r.Ok),
            PostgresOk: results.TryGetValue("postgres", out var pg) && pg.Ok,
            StorageQueueOk: results.TryGetValue("storage_queue", out var sq) && sq.Ok,
            ToolStoreOk: results.TryGetValue("tool_store", out var ts) && ts.Ok,
            LastCheckedAt: DateTime.UtcNow,
            PostgresError: results.TryGetValue("postgres", out var pgErr) ? pgErr.Error : null,
            StorageQueueError: results.TryGetValue("storage_queue", out var sqErr) ? sqErr.Error : null,
            ToolStoreError: results.TryGetValue("tool_store", out var tsErr) ? tsErr.Error : null);

        _state.Update(snapshot);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.DependencyCheckIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCheckAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dependency health check iteration failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
