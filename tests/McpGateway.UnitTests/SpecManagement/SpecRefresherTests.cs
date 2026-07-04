using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using Microsoft.Extensions.Logging.Abstractions;

namespace McpGateway.UnitTests.SpecManagement;

public class SpecRefresherTests
{
    [Fact]
    public async Task RefreshAsync_UnknownServer_ReturnsFailedOutcome()
    {
        var repo = new InMemoryServerDefinitionRepository();
        var refresher = new SpecRefresher(
            repo,
            singleRefresher: null!,
            NullLogger<SpecRefresher>.Instance);

        var outcome = await refresher.RefreshAsync("nonexistent");

        outcome.Status.Should().Be(SpecRefreshStatus.Failed);
        outcome.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task RefreshAsync_ChangedSpec_UpdatesAndRemovesFromStore()
    {
        var serverRepo = new InMemoryServerDefinitionRepository();
        var versionRepo = new InMemorySpecVersionRepository();
        var toolStore = new InMemoryToolStore();

        const string v1 = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"},"paths":{"/users":{"get":{"operationId":"listUsers","summary":"List","responses":{"200":{"description":"OK"}}}}}}""";
        const string v2 = """{"openapi":"3.0.0","info":{"title":"T","version":"2.0"},"paths":{"/users":{"get":{"operationId":"listUsers","summary":"List (v2)","responses":{"200":{"description":"OK"}}}},"/invoices":{"get":{"operationId":"listInvoices","summary":"List invoices","responses":{"200":{"description":"OK"}}}}}}""";

        var server = new McpServerDefinition
        {
            Name = "spec-test",
            DisplayName = "Spec Test",
            SpecSourceUrl = "https://example.com/spec.json",
            SpecContent = v1,
            SpecHash = "old-hash",
            BaseUrl = "https://example.com",
            ClientProfile = ClientProfile.Universal,
            ApprovalStatus = "approved",
            Status = "active"
        };
        await serverRepo.AddAsync(server);

        toolStore.AddServer(server);
        toolStore.Contains("spec-test").Should().BeTrue();

        var fetcher = new StaticSpecFetcher(new FetchedSpec(v2, "new-hash", SpecFormat.Json));
        var diff = new SpecDiffService(new ToolGenerator());
        var single = new ServerSpecRefresher(
            fetcher, diff, new ToolGenerator(), serverRepo, versionRepo, toolStore);
        var refresher = new SpecRefresher(serverRepo, single, NullLogger<SpecRefresher>.Instance);

        var outcome = await refresher.RefreshAsync("spec-test");

        outcome.Status.Should().Be(SpecRefreshStatus.Updated);
        outcome.NewHash.Should().Be("new-hash");
        toolStore.Contains("spec-test").Should().BeFalse();

        var stored = await serverRepo.GetByNameAsync("spec-test");
        stored!.SpecHash.Should().Be("new-hash");
        stored.ApprovalStatus.Should().Be("changes_pending");
        stored.ApprovedAt.Should().BeNull();

        var versions = await versionRepo.ListByServerAsync(server.Id);
        versions.Should().ContainSingle();
        versions[0].SpecHash.Should().Be("new-hash");
        versions[0].ToolCount.Should().Be(2);
    }

    [Fact]
    public async Task RefreshAsync_UnchangedSpec_DoesNotRemoveFromStore()
    {
        var serverRepo = new InMemoryServerDefinitionRepository();
        var versionRepo = new InMemorySpecVersionRepository();
        var toolStore = new InMemoryToolStore();

        const string sameSpec = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"},"paths":{}}""";
        var server = new McpServerDefinition
        {
            Name = "unchanged",
            DisplayName = "Unchanged",
            SpecSourceUrl = "https://example.com/spec.json",
            SpecContent = sameSpec,
            SpecHash = "same-hash",
            BaseUrl = "https://example.com",
            Status = "active"
        };
        await serverRepo.AddAsync(server);
        toolStore.AddServer(server);

        var fetcher = new StaticSpecFetcher(new FetchedSpec(sameSpec, "same-hash", SpecFormat.Json));
        var diff = new SpecDiffService(new ToolGenerator());
        var single = new ServerSpecRefresher(
            fetcher, diff, new ToolGenerator(), serverRepo, versionRepo, toolStore);
        var refresher = new SpecRefresher(serverRepo, single, NullLogger<SpecRefresher>.Instance);

        var outcome = await refresher.RefreshAsync("unchanged");

        outcome.Status.Should().Be(SpecRefreshStatus.Unchanged);
        toolStore.Contains("unchanged").Should().BeTrue();
    }

    private sealed class StaticSpecFetcher : ISpecFetcher
    {
        private readonly FetchedSpec _next;
        public StaticSpecFetcher(FetchedSpec next) => _next = next;
        public Task<FetchedSpec> FetchAsync(SpecSource source, CancellationToken ct = default)
            => Task.FromResult(_next);
    }

    private sealed class InMemoryServerDefinitionRepository : IServerDefinitionRepository
    {
        private readonly Dictionary<Guid, McpServerDefinition> _store = new();

        public Task<McpServerDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_store.Values.FirstOrDefault(s => s.Name == name));

        public Task<McpServerDefinition?> GetByNameForAdminAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_store.Values.FirstOrDefault(s => s.Name == name));

        public Task<IReadOnlyList<McpServerDefinition>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpServerDefinition>>(_store.Values.ToList());

        public Task<IReadOnlyList<McpServerDefinition>> ListApprovedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpServerDefinition>>(
                _store.Values.Where(s => s.ApprovalStatus == "approved").ToList());

        public async Task<McpServerDefinition> AddAsync(McpServerDefinition definition, CancellationToken ct = default)
        {
            definition.Id = Guid.NewGuid();
            _store[definition.Id] = definition;
            return await Task.FromResult(definition);
        }

        public Task UpdateAsync(McpServerDefinition definition, CancellationToken ct = default)
        {
            _store[definition.Id] = definition;
            return Task.CompletedTask;
        }

        public Task UpdateToolsAsync(Guid serverDefinitionId, IEnumerable<ToolDefinition> tools, CancellationToken ct = default)
        {
            if (_store.TryGetValue(serverDefinitionId, out var server))
            {
                server.Tools = tools.ToList();
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            _store.Remove(id);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySpecVersionRepository : ISpecVersionRepository
    {
        private readonly Dictionary<Guid, SpecVersion> _store = new();

        public Task<SpecVersion> AddAsync(SpecVersion version, CancellationToken ct = default)
        {
            version.Id = Guid.NewGuid();
            _store[version.Id] = version;
            return Task.FromResult(version);
        }

        public Task<SpecVersion?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_store.TryGetValue(id, out var v) ? v : null);

        public Task<IReadOnlyList<SpecVersion>> ListByServerAsync(Guid serverDefinitionId, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SpecVersion>>(
                _store.Values.Where(v => v.ServerDefinitionId == serverDefinitionId).ToList());

        public Task<SpecVersion?> GetLatestAsync(Guid serverDefinitionId, CancellationToken ct = default)
            => Task.FromResult(_store.Values
                .Where(v => v.ServerDefinitionId == serverDefinitionId)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefault());
    }
}
