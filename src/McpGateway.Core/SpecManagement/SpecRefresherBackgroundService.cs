using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace McpGateway.Core.SpecManagement;

public class SpecRefresherBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpecRefresherBackgroundService> _logger;
    private readonly TimeSpan _tickInterval;

    public SpecRefresherBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SpecRefresherBackgroundService> logger,
        TimeSpan? tickInterval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _tickInterval = tickInterval ?? TimeSpan.FromMinutes(1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpecRefresher background service started; tick interval {Interval}", _tickInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var refresher = scope.ServiceProvider.GetRequiredService<ISpecRefresher>();
                var outcomes = await refresher.RefreshAllAsync(stoppingToken);
                foreach (var outcome in outcomes)
                {
                    LogOutcome(outcome);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SpecRefresher tick failed");
            }

            try
            {
                await Task.Delay(_tickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("SpecRefresher background service stopped");
    }

    private void LogOutcome(SpecRefreshOutcome outcome)
    {
        switch (outcome.Status)
        {
            case SpecRefreshStatus.Updated:
                _logger.LogInformation(
                    "Spec refresh: {Server} updated ({Old} -> {New})",
                    outcome.ServerName, outcome.OldHash, outcome.NewHash);
                break;
            case SpecRefreshStatus.Unchanged:
                _logger.LogDebug(
                    "Spec refresh: {Server} unchanged ({Hash})",
                    outcome.ServerName, outcome.NewHash);
                break;
            case SpecRefreshStatus.Failed:
                _logger.LogWarning(
                    "Spec refresh: {Server} failed: {Error}",
                    outcome.ServerName, outcome.Error);
                break;
            case SpecRefreshStatus.NoSpecSource:
                _logger.LogDebug(
                    "Spec refresh: {Server} has no SpecSourceUrl, skipping",
                    outcome.ServerName);
                break;
        }
    }
}
