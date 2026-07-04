using FluentAssertions;
using McpGateway.Core.Health;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace McpGateway.UnitTests.Health;

public class DependencyHealthCheckerTests
{
    [Fact]
    public async Task RunCheckAsync_AllProbesOk_MarksReady()
    {
        var state = new ReadinessState();
        var store = new InMemoryToolStore();
        store.AddServer(new McpServerDefinition
        {
            Name = "s",
            DisplayName = "S",
            BaseUrl = "https://s.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}"
        });

        var probes = new IDependencyProbe[]
        {
            new SuccessProbe("postgres"),
            new SuccessProbe("storage_queue"),
            new ToolStoreDependencyProbe(store)
        };

        var checker = new DependencyHealthChecker(
            state,
            probes,
            Options.Create(new HealthOptions()),
            NullLogger<DependencyHealthChecker>.Instance);

        await checker.RunCheckAsync();

        state.Current.IsReady.Should().BeTrue();
        state.Current.PostgresOk.Should().BeTrue();
        state.Current.StorageQueueOk.Should().BeTrue();
        state.Current.ToolStoreOk.Should().BeTrue();
        state.Current.LastCheckedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunCheckAsync_AnyProbeFails_MarksNotReady()
    {
        var state = new ReadinessState();
        var store = new InMemoryToolStore();
        store.AddServer(new McpServerDefinition
        {
            Name = "s",
            DisplayName = "S",
            BaseUrl = "https://s.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}"
        });

        var probes = new IDependencyProbe[]
        {
            new SuccessProbe("postgres"),
            new FailureProbe("storage_queue", "queue down"),
            new ToolStoreDependencyProbe(store)
        };

        var checker = new DependencyHealthChecker(
            state,
            probes,
            Options.Create(new HealthOptions()),
            NullLogger<DependencyHealthChecker>.Instance);

        await checker.RunCheckAsync();

        state.Current.IsReady.Should().BeFalse();
        state.Current.StorageQueueOk.Should().BeFalse();
        state.Current.StorageQueueError.Should().Be("queue down");
    }

    [Fact]
    public async Task RunCheckAsync_EmptyToolStore_MarksNotReady()
    {
        var state = new ReadinessState();
        var probes = new IDependencyProbe[]
        {
            new SuccessProbe("postgres"),
            new SuccessProbe("storage_queue"),
            new ToolStoreDependencyProbe(new InMemoryToolStore())
        };

        var checker = new DependencyHealthChecker(
            state,
            probes,
            Options.Create(new HealthOptions()),
            NullLogger<DependencyHealthChecker>.Instance);

        await checker.RunCheckAsync();

        state.Current.IsReady.Should().BeFalse();
        state.Current.ToolStoreOk.Should().BeFalse();
    }

    [Fact]
    public async Task RunCheckAsync_PropagatesIndividualProbeErrors()
    {
        var state = new ReadinessState();
        var probes = new IDependencyProbe[]
        {
            new FailureProbe("postgres", "pg down"),
            new FailureProbe("storage_queue", "queue down"),
            new FailureProbe("tool_store", "no servers")
        };

        var checker = new DependencyHealthChecker(
            state,
            probes,
            Options.Create(new HealthOptions()),
            NullLogger<DependencyHealthChecker>.Instance);

        await checker.RunCheckAsync();

        state.Current.PostgresError.Should().Be("pg down");
        state.Current.StorageQueueError.Should().Be("queue down");
        state.Current.ToolStoreError.Should().Be("no servers");
    }

    private sealed class SuccessProbe : IDependencyProbe
    {
        public SuccessProbe(string name) { Name = name; }
        public string Name { get; }
        public Task<ProbeResult> ProbeAsync(CancellationToken ct = default)
            => Task.FromResult(ProbeResult.Success());
    }

    private sealed class FailureProbe : IDependencyProbe
    {
        public FailureProbe(string name, string error) { Name = name; _error = error; }
        public string Name { get; }
        private readonly string _error;
        public Task<ProbeResult> ProbeAsync(CancellationToken ct = default)
            => Task.FromResult(ProbeResult.Failure(_error));
    }
}
