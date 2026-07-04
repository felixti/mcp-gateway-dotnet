using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using McpGateway.Core.Health;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace McpGateway.IntegrationTests.Health;

public class HealthEndpointsTests : IClassFixture<HealthEndpointsTests.Factory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointsTests(Factory factory)
    {
        _factory = factory;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSql"] = "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=1;Command Timeout=1",
                    ["ConnectionStrings:StorageQueue"] = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;QueueEndpoint=http://127.0.0.1:1/devstoreaccount1;"
                });
            });

            builder.ConfigureServices(services =>
            {
                // Strip hosted services that need backing stores we don't have in this test.
                var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
                foreach (var d in hosted)
                {
                    services.Remove(d);
                }
            });
        }
    }

    [Fact]
    public async Task Health_Returns200_WithUptime()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ok");
        body.GetProperty("uptime_seconds").GetInt64().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Ready_NoProbesCompleted_Returns503()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("not_ready");
    }

    [Fact]
    public async Task Ready_AfterMarkingReady_Returns200()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var state = new ReadinessState();
                state.Update(new ReadinessSnapshot(
                    IsReady: true, PostgresOk: true, StorageQueueOk: true, ToolStoreOk: true,
                    LastCheckedAt: DateTime.UtcNow,
                    PostgresError: null, StorageQueueError: null, ToolStoreError: null));
                services.RemoveAll<IReadinessState>();
                services.AddSingleton<IReadinessState>(state);
            });
        }).CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ready");
        body.GetProperty("checks").GetProperty("postgres").GetString().Should().Be("ok");
        body.GetProperty("checks").GetProperty("storage_queue").GetString().Should().Be("ok");
        body.GetProperty("checks").GetProperty("tool_store").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Ready_AfterMarkNotReady_Returns503()
    {
        var state = new ReadinessState();
        state.Update(new ReadinessSnapshot(
            IsReady: true, PostgresOk: true, StorageQueueOk: true, ToolStoreOk: true,
            LastCheckedAt: DateTime.UtcNow,
            PostgresError: null, StorageQueueError: null, ToolStoreError: null));
        state.MarkNotReady("draining");

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IReadinessState>();
                services.AddSingleton<IReadinessState>(state);
            });
        }).CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("not_ready");
    }
}
