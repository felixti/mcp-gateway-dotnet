using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpGateway.Core.Audit;

public class DiskFallbackRetryWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly QueueEmitterOptions _queueOptions;
    private readonly DiskFallback _diskFallback;
    private readonly ILogger<DiskFallbackRetryWorker> _logger;

    public DiskFallbackRetryWorker(
        IOptions<QueueEmitterOptions> queueOptions,
        DiskFallback diskFallback,
        ILogger<DiskFallbackRetryWorker> logger)
    {
        _queueOptions = queueOptions.Value;
        _diskFallback = diskFallback;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit disk fallback drain failed; will retry next tick.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task DrainAsync(CancellationToken ct)
    {
        var queueClient = new QueueClient(_queueOptions.ConnectionString, _queueOptions.QueueName);
        try
        {
            await queueClient.CreateIfNotExistsAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "Audit queue still unavailable; will retry later.");
            return;
        }

        foreach (var file in _diskFallback.GetPendingFiles())
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(file, ct);
                var auditEvent = JsonSerializer.Deserialize<AuditEvent>(bytes)
                    ?? throw new InvalidOperationException($"Cannot deserialize {file}.");

                var base64Encoded = Convert.ToBase64String(bytes);
                await queueClient.SendMessageAsync(base64Encoded, cancellationToken: ct);
                await _diskFallback.RemoveAsync(file, ct);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(ex, "Audit queue rejected message for {File}; leaving on disk.", file);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replay audit event from {File}; leaving on disk.", file);
            }
        }
    }
}
