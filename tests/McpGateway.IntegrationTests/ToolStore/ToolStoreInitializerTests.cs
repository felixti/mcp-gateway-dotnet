using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using McpGateway.Persistence;
using McpGateway.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace McpGateway.IntegrationTests.ToolStore;

[Collection("Persistence")]
public class ToolStoreInitializerTests
{
    private readonly PostgreSqlFixture _fixture;

    public ToolStoreInitializerTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InitializeAsync_LoadsOnlyApprovedServers()
    {
        await using var context = _fixture.CreateDbContext();
        var repo = new ServerDefinitionRepository(context);

        var approved = new McpServerDefinition
        {
            Name = "approved-api",
            DisplayName = "Approved",
            SpecContent = "{}",
            SpecHash = "hash1",
            BaseUrl = "https://approved.example.com",
            ApprovalStatus = "approved"
        };
        var pending = new McpServerDefinition
        {
            Name = "pending-api",
            DisplayName = "Pending",
            SpecContent = "{}",
            SpecHash = "hash2",
            BaseUrl = "https://pending.example.com",
            ApprovalStatus = "pending"
        };

        await repo.AddAsync(approved);
        await repo.AddAsync(pending);

        var store = new InMemoryToolStore();
        var initializer = new ToolStoreInitializer(store, repo);
        await initializer.InitializeAsync();

        store.GetServer("approved-api").Should().NotBeNull();
        store.GetServer("pending-api").Should().BeNull();
    }
}
