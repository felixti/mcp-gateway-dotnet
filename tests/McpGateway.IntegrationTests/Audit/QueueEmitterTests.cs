using Azure;
using FluentAssertions;
using McpGateway.Core.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace McpGateway.IntegrationTests.Audit;

[Collection("Audit")]
public class QueueEmitterTests
{
    private readonly AzuriteFixture _fixture;
    private readonly string _queueName = $"mcp-audit-{Guid.NewGuid():N}";

    public QueueEmitterTests(AzuriteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EmitAsync_SendsBase64JsonMessage()
    {
        var queue = _fixture.CreateQueueClient(_queueName);
        await queue.CreateIfNotExistsAsync();

        var diskFallback = CreateDiskFallback();
        var emitter = new QueueEmitter(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = _queueName
            }),
            diskFallback,
            NullLogger<QueueEmitter>.Instance,
            TimeProvider.System);

        var auditEvent = new AuditEvent
        {
            CallerId = "user-1",
            CallerName = "alice@example.com",
            GatewayAuthMethod = "EntraIdJwt",
            AuthStrategy = "obo",
            ServerName = "invoice-api",
            ToolName = "get_invoices",
            Arguments = "{\"limit\":10}",
            Response = "{\"items\":[]}",
            HttpStatus = 200,
            IsError = false,
            LatencyMs = 42
        };

        await emitter.EmitAsync(auditEvent);

        var messages = await queue.ReceiveMessagesAsync(maxMessages: 10);
        messages.Value.Length.Should().Be(1);
        var payload = messages.Value[0].Body.ToString();
        // Body is the base64-encoded JSON; decode before deserializing.
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AuditEvent>(decoded)!;
        roundTrip.CallerId.Should().Be("user-1");
        roundTrip.ServerName.Should().Be("invoice-api");
        roundTrip.ToolName.Should().Be("get_invoices");
    }

    [Fact]
    public async Task EmitAsync_TruncatesResponseTo10Kb()
    {
        var queue = _fixture.CreateQueueClient(_queueName);
        await queue.CreateIfNotExistsAsync();

        var diskFallback = CreateDiskFallback();
        var emitter = new QueueEmitter(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = _queueName
            }),
            diskFallback,
            NullLogger<QueueEmitter>.Instance,
            TimeProvider.System);

        var auditEvent = new AuditEvent
        {
            ServerName = "invoice-api",
            ToolName = "get_invoices",
            Response = new string('x', 20_000)
        };

        await emitter.EmitAsync(auditEvent);

        var messages = await queue.ReceiveMessagesAsync(maxMessages: 10);
        var payload = messages.Value[0].Body.ToString();
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<AuditEvent>(decoded)!;
        roundTrip.Response.Length.Should().BeLessThanOrEqualTo(QueueEmitterOptions.MaxResponseBytes);
    }

    [Fact]
    public async Task EmitAsync_WhenQueueMissing_FallsBackToDisk()
    {
        var diskFallback = CreateDiskFallback();
        // Queue name is invalid (uppercase), so CreateIfNotExistsAsync returns
        // 400 InvalidResourceName. The emitter's generic catch routes the
        // event to disk fallback. This simulates the "queue unavailable"
        // path the production emitter is designed to handle.
        var emitter = new QueueEmitter(
            Options.Create(new QueueEmitterOptions
            {
                ConnectionString = _fixture.ConnectionString,
                QueueName = "INVALID-QUEUE-NAME"
            }),
            diskFallback,
            NullLogger<QueueEmitter>.Instance,
            TimeProvider.System);

        var auditEvent = new AuditEvent
        {
            ServerName = "invoice-api",
            ToolName = "get_invoices",
            EventId = Guid.NewGuid().ToString("N")
        };

        await emitter.EmitAsync(auditEvent);

        var pending = diskFallback.GetPendingFiles();
        pending.Should().ContainSingle();
    }

    private DiskFallback CreateDiskFallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"audit-fallback-{Guid.NewGuid():N}");
        return new DiskFallback(
            Options.Create(new DiskFallbackOptions { Directory = directory }),
            NullLogger<DiskFallback>.Instance);
    }
}
