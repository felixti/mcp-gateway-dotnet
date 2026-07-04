using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpGateway.Core.Audit;

public class DiskFallback
{
    private readonly DiskFallbackOptions _options;
    private readonly ILogger<DiskFallback> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentQueue<string> _files = new();

    public DiskFallback(IOptions<DiskFallbackOptions> options, ILogger<DiskFallback> logger)
    {
        _options = options.Value;
        _logger = logger;

        Directory.CreateDirectory(_options.Directory);
        LoadExistingFiles();
    }

    public async Task BufferAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(auditEvent);
        var fileName = $"{auditEvent.EventId}.json";
        var path = Path.Combine(_options.Directory, fileName);

        await _writeLock.WaitAsync(ct);
        try
        {
            if (DirectorySizeBytes() + payload.Length > _options.MaxBufferBytes)
            {
                _logger.LogError(
                    "Audit disk fallback buffer exceeded {MaxBytes} bytes; dropping event {EventId}.",
                    _options.MaxBufferBytes,
                    auditEvent.EventId);
                return;
            }

            await File.WriteAllBytesAsync(path, payload, ct);
            _files.Enqueue(path);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public IReadOnlyCollection<string> GetPendingFiles()
    {
        return _files.Where(File.Exists).ToList().AsReadOnly();
    }

    public async Task RemoveAsync(string path, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void LoadExistingFiles()
    {
        foreach (var path in Directory.EnumerateFiles(_options.Directory, "*.json"))
        {
            _files.Enqueue(path);
        }
    }

    private long DirectorySizeBytes()
    {
        return new DirectoryInfo(_options.Directory)
            .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
            .Sum(f => f.Length);
    }
}
