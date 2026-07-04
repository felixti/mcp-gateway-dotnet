using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpGateway.Core.Audit;

public class QueueEmitter : IAuditEmitter
{
    private readonly QueueClient _queueClient;
    private readonly DiskFallback _diskFallback;
    private readonly ILogger<QueueEmitter> _logger;
    private readonly TimeProvider _timeProvider;

    public QueueEmitter(
        IOptions<QueueEmitterOptions> options,
        DiskFallback diskFallback,
        ILogger<QueueEmitter> logger,
        TimeProvider timeProvider)
    {
        var opts = options.Value;
        _queueClient = new QueueClient(opts.ConnectionString, opts.QueueName);
        _diskFallback = diskFallback;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task EmitAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        auditEvent.Response = Truncate(auditEvent.Response, QueueEmitterOptions.MaxResponseBytes);

        var payload = JsonSerializer.SerializeToUtf8Bytes(auditEvent);

        try
        {
            await _queueClient.CreateIfNotExistsAsync(cancellationToken: ct);
            var base64Encoded = Convert.ToBase64String(payload);
            await _queueClient.SendMessageAsync(base64Encoded, cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning(ex, "Audit queue not found; buffering to disk.");
            await _diskFallback.BufferAsync(auditEvent, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue audit event {EventId}; buffering to disk.", auditEvent.EventId);
            await _diskFallback.BufferAsync(auditEvent, ct);
        }
    }

    private static string Truncate(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
        {
            return value;
        }

        return Encoding.UTF8.GetString(bytes, 0, maxBytes);
    }
}
