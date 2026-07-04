using Azure;
using FluentAssertions;
using McpGateway.Core.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace McpGateway.IntegrationTests.Audit;

[Collection("Audit")]
public class AuditPipelineTests
{
    private readonly AzuriteFixture _fixture;
    private readonly string _queueName = $"mcp-audit-{Guid.NewGuid():N}";

    public AuditPipelineTests(AzuriteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DiskFallbackRetryWorker_DrainsBufferIntoQueue()
    {
        var diskDirectory = Path.Combine(Path.GetTempPath(), $"audit-fallback-{Guid.NewGuid():N}");
        var diskFallback = new DiskFallback(
            Options.Create(new DiskFallbackOptions { Directory = diskDirectory }),
            NullLogger<DiskFallback>.Instance);

        await diskFallback.BufferAsync(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            ServerName = "invoice-api",
            ToolName = "get_invoices"
        });
        await diskFallback.BufferAsync(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            ServerName = "invoice-api",
            ToolName = "get_invoice"
        });

        var worker = new DiskFallbackRetryWorker(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = _queueName
            }),
            diskFallback,
            NullLogger<DiskFallbackRetryWorker>.Instance);

        var queue = _fixture.CreateQueueClient(_queueName);
        await queue.CreateIfNotExistsAsync();

        await worker.DrainAsync(CancellationToken.None);

        var messages = await queue.ReceiveMessagesAsync(maxMessages: 10);
        messages.Value.Length.Should().Be(2);
        diskFallback.GetPendingFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task DiskFallbackRetryWorker_WhenQueueMissing_LeavesBufferIntact()
    {
        var diskDirectory = Path.Combine(Path.GetTempPath(), $"audit-fallback-{Guid.NewGuid():N}");
        var diskFallback = new DiskFallback(
            Options.Create(new DiskFallbackOptions { Directory = diskDirectory }),
            NullLogger<DiskFallback>.Instance);

        await diskFallback.BufferAsync(new AuditEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            ServerName = "invoice-api",
            ToolName = "get_invoices"
        });

        // Invalid queue name (uppercase) -> CreateIfNotExistsAsync fails
        // with 400 -> worker logs warning and leaves the buffer intact.
        var worker = new DiskFallbackRetryWorker(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = "INVALID-QUEUE-NAME"
            }),
            diskFallback,
            NullLogger<DiskFallbackRetryWorker>.Instance);

        await worker.DrainAsync(CancellationToken.None);

        diskFallback.GetPendingFiles().Should().ContainSingle();
    }
}
