using FluentAssertions;
using McpGateway.Core.Health;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace McpGateway.UnitTests.Health;

public class GracefulShutdownServiceTests
{
    [Fact]
    public async Task StopAsync_MarksNotReadyBeforeFlush()
    {
        var state = new ReadinessState();
        var tracker = new InFlightCallTracker();
        var audit = new RecordingAuditFlusher();
        var disk = new RecordingDiskFallbackFlusher();
        var lifetime = new TestApplicationLifetime();

        var service = new GracefulShutdownService(
            state, tracker, audit, disk,
            Options.Create(new HealthOptions { ShutdownDrainTimeoutSeconds = 1 }),
            lifetime,
            NullLogger<GracefulShutdownService>.Instance);

        await service.StartAsync(CancellationToken.None);
        state.Update(new ReadinessSnapshot(
            IsReady: true, PostgresOk: true, StorageQueueOk: true, ToolStoreOk: true,
            LastCheckedAt: DateTime.UtcNow, PostgresError: null, StorageQueueError: null, ToolStoreError: null));

        lifetime.NotifyStopping();

        await service.StopAsync(CancellationToken.None);

        state.Current.IsReady.Should().BeFalse();
        audit.Flushed.Should().BeTrue();
        disk.Flushed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WaitsForInFlightCalls_ThenFlushes()
    {
        var state = new ReadinessState();
        var tracker = new InFlightCallTracker();
        var audit = new RecordingAuditFlusher();
        var disk = new RecordingDiskFallbackFlusher();
        var lifetime = new TestApplicationLifetime();
        var inFlight = tracker.Begin();

        var service = new GracefulShutdownService(
            state, tracker, audit, disk,
            Options.Create(new HealthOptions { ShutdownDrainTimeoutSeconds = 5 }),
            lifetime,
            NullLogger<GracefulShutdownService>.Instance);

        await service.StartAsync(CancellationToken.None);
        lifetime.NotifyStopping();
        var stopTask = service.StopAsync(CancellationToken.None);

        await Task.Delay(100);
        audit.Flushed.Should().BeFalse();

        inFlight.Dispose();
        await stopTask;

        audit.Flushed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_TimesOutWhenCallsRemain()
    {
        var state = new ReadinessState();
        var tracker = new InFlightCallTracker();
        var audit = new RecordingAuditFlusher();
        var disk = new RecordingDiskFallbackFlusher();
        var lifetime = new TestApplicationLifetime();
        using var inFlight = tracker.Begin();

        var service = new GracefulShutdownService(
            state, tracker, audit, disk,
            Options.Create(new HealthOptions { ShutdownDrainTimeoutSeconds = 1 }),
            lifetime,
            NullLogger<GracefulShutdownService>.Instance);

        await service.StartAsync(CancellationToken.None);
        lifetime.NotifyStopping();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        sw.Stop();

        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(800));
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        audit.Flushed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_ContinuesEvenIfAuditFlushThrows()
    {
        var state = new ReadinessState();
        var tracker = new InFlightCallTracker();
        var disk = new RecordingDiskFallbackFlusher();
        var lifetime = new TestApplicationLifetime();

        var service = new GracefulShutdownService(
            state, tracker, new ThrowingAuditFlusher(), disk,
            Options.Create(new HealthOptions { ShutdownDrainTimeoutSeconds = 1 }),
            lifetime,
            NullLogger<GracefulShutdownService>.Instance);

        await service.StartAsync(CancellationToken.None);
        lifetime.NotifyStopping();

        await service.StopAsync(CancellationToken.None);

        disk.Flushed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_ContinuesEvenIfDiskFlushThrows()
    {
        var state = new ReadinessState();
        var tracker = new InFlightCallTracker();
        var audit = new RecordingAuditFlusher();
        var lifetime = new TestApplicationLifetime();

        var service = new GracefulShutdownService(
            state, tracker, audit, new ThrowingDiskFallbackFlusher(),
            Options.Create(new HealthOptions { ShutdownDrainTimeoutSeconds = 1 }),
            lifetime,
            NullLogger<GracefulShutdownService>.Instance);

        await service.StartAsync(CancellationToken.None);
        lifetime.NotifyStopping();

        await service.StopAsync(CancellationToken.None);

        audit.Flushed.Should().BeTrue();
    }

    private sealed class RecordingAuditFlusher : IAuditFlusher
    {
        public bool Flushed { get; private set; }
        public Task FlushAsync(CancellationToken ct = default)
        {
            Flushed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDiskFallbackFlusher : IDiskFallbackFlusher
    {
        public bool Flushed { get; private set; }
        public Task FlushAsync(CancellationToken ct = default)
        {
            Flushed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAuditFlusher : IAuditFlusher
    {
        public Task FlushAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("audit down");
    }

    private sealed class ThrowingDiskFallbackFlusher : IDiskFallbackFlusher
    {
        public Task FlushAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("disk down");
    }

    private sealed class TestApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();
        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;
        public void NotifyStopping() => _stopping.Cancel();
        public void NotifyStopped() => _stopped.Cancel();
        public void StopApplication() => NotifyStopping();
    }
}
