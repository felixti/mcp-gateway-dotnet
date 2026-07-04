using FluentAssertions;
using McpGateway.Core.Health;
using McpGateway.IntegrationTests.Health.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace McpGateway.IntegrationTests.Health;

[Collection("Health")]
public class ShutdownIntegrationTests
{
    private readonly PostgreSqlHealthFixture _pg;
    private readonly AzuriteHealthFixture _azurite;

    public ShutdownIntegrationTests(PostgreSqlHealthFixture pg, AzuriteHealthFixture azurite)
    {
        _pg = pg;
        _azurite = azurite;
    }

    [Fact]
    public async Task Readiness_AfterGracefulShutdownServiceRuns_Returns503()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:PostgreSql"] = _pg.ConnectionString,
                        ["ConnectionStrings:StorageQueue"] = _azurite.ConnectionString
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Strip hosted services that need additional backing stores we don't have here.
                    var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
                    foreach (var d in hosted)
                    {
                        services.Remove(d);
                    }
                    // Re-register only the graceful shutdown service so the lifetime hook fires.
                    services.AddHostedService<GracefulShutdownService>();
                });
            });

        var client = factory.CreateClient();
        var state = factory.Services.GetRequiredService<IReadinessState>();
        var tracker = factory.Services.GetRequiredService<IInFlightCallTracker>();
        var audit = factory.Services.GetRequiredService<IAuditFlusher>();
        var disk = factory.Services.GetRequiredService<IDiskFallbackFlusher>();
        var options = factory.Services.GetRequiredService<IOptions<HealthOptions>>();

        state.Update(new ReadinessSnapshot(
            IsReady: true, PostgresOk: true, StorageQueueOk: true, ToolStoreOk: true,
            LastCheckedAt: DateTime.UtcNow,
            PostgresError: null, StorageQueueError: null, ToolStoreError: null));

        var readyResponse = await client.GetAsync("/ready");
        readyResponse.IsSuccessStatusCode.Should().BeTrue();

        // Drive the shutdown service through a local lifetime so we do not tear down the test host.
        using var localCts = new CancellationTokenSource();
        var localLifetime = new LocalLifetime(localCts);
        var service = new GracefulShutdownService(
            state, tracker, audit, disk, options, localLifetime,
            NullLogger<GracefulShutdownService>.Instance);
        await service.StartAsync(CancellationToken.None);
        localLifetime.NotifyStopping();
        await service.StopAsync(CancellationToken.None);

        var postStopResponse = await client.GetAsync("/ready");
        postStopResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    private sealed class LocalLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping;
        public LocalLifetime(CancellationTokenSource stopping) { _stopping = stopping; }
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void NotifyStopping() => _stopping.Cancel();
        public void StopApplication() => NotifyStopping();
    }
}
