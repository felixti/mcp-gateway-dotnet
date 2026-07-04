using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpGateway.Core.Health;

public class GracefulShutdownService : IHostedService
{
    private readonly IReadinessState _readinessState;
    private readonly IInFlightCallTracker _inFlightTracker;
    private readonly IAuditFlusher _auditFlusher;
    private readonly IDiskFallbackFlusher _diskFallbackFlusher;
    private readonly HealthOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<GracefulShutdownService> _logger;

    public GracefulShutdownService(
        IReadinessState readinessState,
        IInFlightCallTracker inFlightTracker,
        IAuditFlusher auditFlusher,
        IDiskFallbackFlusher diskFallbackFlusher,
        IOptions<HealthOptions> options,
        IHostApplicationLifetime lifetime,
        ILogger<GracefulShutdownService> logger)
    {
        _readinessState = readinessState;
        _inFlightTracker = inFlightTracker;
        _auditFlusher = auditFlusher;
        _diskFallbackFlusher = diskFallbackFlusher;
        _options = options.Value;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _lifetime.ApplicationStopping.Register(() => OnStopping());
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DrainAsync(cancellationToken);
    }

    private void OnStopping()
    {
        _readinessState.MarkNotReady("application stopping");
        _logger.LogInformation("Readiness marked not-ready; shutdown initiated. In-flight calls: {Count}", _inFlightTracker.InFlightCount);
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        var drainTimeout = TimeSpan.FromSeconds(Math.Max(1, _options.ShutdownDrainTimeoutSeconds));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _inFlightTracker.WaitForDrainAsync(drainTimeout, ct);
        sw.Stop();

        if (_inFlightTracker.InFlightCount > 0)
        {
            _logger.LogWarning("Shutdown drain timed out after {Elapsed}ms with {Count} in-flight calls.", sw.ElapsedMilliseconds, _inFlightTracker.InFlightCount);
        }
        else
        {
            _logger.LogInformation("In-flight calls drained in {Elapsed}ms.", sw.ElapsedMilliseconds);
        }

        try
        {
            await _auditFlusher.FlushAsync(ct);
            _logger.LogInformation("Audit events flushed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit flush failed during shutdown.");
        }

        try
        {
            await _diskFallbackFlusher.FlushAsync(ct);
            _logger.LogInformation("Disk fallback flushed.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disk fallback flush failed during shutdown.");
        }
    }
}
