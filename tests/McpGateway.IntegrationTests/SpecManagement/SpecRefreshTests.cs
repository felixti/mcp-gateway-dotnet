using System.Net;
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace McpGateway.IntegrationTests.SpecManagement;

[Collection("Persistence")]
public class SpecRefreshTests
{
    private readonly PostgreSqlFixture _fixture;

    public SpecRefreshTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Refresh_ChangedSpec_UpdatesServerDefAndClearsToolStore()
    {
        var initialServerRepo = new ServerDefinitionRepository(_fixture.CreateDbContext());
        var versionRepo = new SpecVersionRepository(_fixture.CreateDbContext());
        var toolStore = new InMemoryToolStore();

        const string v1 = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"},"paths":{"/users":{"get":{"operationId":"listUsers","summary":"List users","responses":{"200":{"description":"OK"}}}}}}""";
        const string v2 = """{"openapi":"3.0.0","info":{"title":"T","version":"2.0"},"paths":{"/users":{"get":{"operationId":"listUsers","summary":"List users (v2)","responses":{"200":{"description":"OK"}}}},"/invoices":{"get":{"operationId":"listInvoices","summary":"List invoices","responses":{"200":{"description":"OK"}}}}}}""";

        var server = await initialServerRepo.AddAsync(new McpServerDefinition
        {
            Name = "refresh-e2e",
            DisplayName = "Refresh E2E",
            SpecSourceUrl = "https://specs.example.com/refresh-e2e.json",
            SpecContent = v1,
            SpecHash = ComputeHash(v1),
            BaseUrl = "https://api.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}",
            ClientProfile = ClientProfile.Universal,
            ApprovalStatus = "approved",
            Status = "active"
        });

        var httpHandler = new Mock<HttpMessageHandler>();
        httpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(v2, System.Text.Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(httpHandler.Object);
        var httpFactory = new StaticHttpClientFactory(httpClient);
        var fetcher = new SpecFetcher(httpFactory);

        var serverRepo = new ServerDefinitionRepository(_fixture.CreateDbContext());
        var diff = new SpecDiffService(new ToolGenerator());
        var single = new ServerSpecRefresher(
            fetcher, diff, new ToolGenerator(), serverRepo, versionRepo, toolStore);
        var refresher = new SpecRefresher(serverRepo, single, NullLogger<SpecRefresher>.Instance);

        toolStore.AddServer(server);
        toolStore.Contains("refresh-e2e").Should().BeTrue();

        var outcome = await refresher.RefreshAsync("refresh-e2e");

        outcome.Status.Should().Be(SpecRefreshStatus.Updated);
        toolStore.Contains("refresh-e2e").Should().BeFalse();

        await using var verifyContext = _fixture.CreateDbContext();
        var verifyRepo = new ServerDefinitionRepository(verifyContext);
        var stored = await verifyRepo.GetByNameAsync("refresh-e2e");
        stored.Should().NotBeNull();
        stored!.SpecHash.Should().NotBe(server.SpecHash);
        stored.ApprovalStatus.Should().Be("changes_pending");
        stored.ApprovedAt.Should().BeNull();
        stored.LastRefreshedAt.Should().NotBeNull();

        var versions = await versionRepo.ListByServerAsync(server.Id);
        versions.Should().ContainSingle();
        versions[0].ToolCount.Should().Be(2);
    }

    [Fact]
    public async Task Refresh_UnchangedSpec_DoesNotChangeApprovalStatus()
    {
        var initialServerRepo = new ServerDefinitionRepository(_fixture.CreateDbContext());
        var versionRepo = new SpecVersionRepository(_fixture.CreateDbContext());
        var toolStore = new InMemoryToolStore();

        const string same = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"},"paths":{"/a":{"get":{"operationId":"a","summary":"A","responses":{"200":{"description":"OK"}}}}}}""";
        var server = await initialServerRepo.AddAsync(new McpServerDefinition
        {
            Name = "unchanged-e2e",
            DisplayName = "Unchanged E2E",
            SpecSourceUrl = "https://specs.example.com/u.json",
            SpecContent = same,
            SpecHash = ComputeHash(same),
            BaseUrl = "https://api.example.com",
            ApprovalStatus = "approved",
            Status = "active"
        });

        var httpHandler = new Mock<HttpMessageHandler>();
        httpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(same, System.Text.Encoding.UTF8, "application/json")
            });

        using var httpClient = new HttpClient(httpHandler.Object);
        var fetcher = new SpecFetcher(new StaticHttpClientFactory(httpClient));
        var serverRepo = new ServerDefinitionRepository(_fixture.CreateDbContext());
        var diff = new SpecDiffService(new ToolGenerator());
        var single = new ServerSpecRefresher(
            fetcher, diff, new ToolGenerator(), serverRepo, versionRepo, toolStore);
        var refresher = new SpecRefresher(serverRepo, single, NullLogger<SpecRefresher>.Instance);

        toolStore.AddServer(server);

        var outcome = await refresher.RefreshAsync("unchanged-e2e");

        outcome.Status.Should().Be(SpecRefreshStatus.Unchanged);
        toolStore.Contains("unchanged-e2e").Should().BeTrue();

        await using var verifyContext = _fixture.CreateDbContext();
        var verifyRepo = new ServerDefinitionRepository(verifyContext);
        var stored = await verifyRepo.GetByNameAsync("unchanged-e2e");
        stored!.ApprovalStatus.Should().Be("approved");
    }

    private static string ComputeHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StaticHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
