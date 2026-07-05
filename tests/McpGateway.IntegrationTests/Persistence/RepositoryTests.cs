using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Persistence;
using McpGateway.Persistence.Repositories;

namespace McpGateway.IntegrationTests.Persistence;

[Collection("Persistence")]
public class RepositoryTests
{
    private readonly PostgreSqlFixture _fixture;

    public RepositoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    private IServerDefinitionRepository CreateServerRepo()
    {
        return new ServerDefinitionRepository(_fixture.CreateDbContext());
    }

    private IGatewayApiKeyRepository CreateApiKeyRepo()
    {
        return new GatewayApiKeyRepository(_fixture.CreateDbContext());
    }

    private IToolOverrideRepository CreateOverrideRepo()
    {
        return new ToolOverrideRepository(_fixture.CreateDbContext());
    }

    [Fact]
    public async Task ServerDefinitionRepository_AddAndGetByName()
    {
        var repo = CreateServerRepo();
        var server = new McpServerDefinition
        {
            Name = "repo-test",
            DisplayName = "Repo Test",
            SpecContent = "{}",
            SpecHash = "hash",
            BaseUrl = "https://repo.example.com"
        };

        var added = await repo.AddAsync(server);
        var retrieved = await repo.GetByNameAsync("repo-test");

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(added.Id);
        retrieved.Name.Should().Be("repo-test");
    }

    [Fact]
    public async Task ServerDefinitionRepository_McpUpstreamServerWithNullToolCoords_RoundTrips()
    {
        var repo = CreateServerRepo();
        var server = new McpServerDefinition
        {
            Name = "mcp-upstream-test",
            DisplayName = "MCP Upstream Test",
            SpecContent = "{}",
            SpecHash = "hash",
            BaseUrl = "https://mcp.example.com",
            SourceType = SourceType.McpUpstream,
            Tools =
            [
                new ToolDefinition
                {
                    ToolName = "upstream_tool",
                    Description = "Upstream tool without HTTP coords",
                    InputSchema = "{}",
                    HttpMethod = null,
                    HttpPath = null
                }
            ]
        };

        var added = await repo.AddAsync(server);
        var retrieved = await repo.GetByNameAsync("mcp-upstream-test");

        retrieved.Should().NotBeNull();
        retrieved!.SourceType.Should().Be(SourceType.McpUpstream);
        retrieved.Tools.Should().ContainSingle();
        var tool = retrieved.Tools.Single();
        tool.HttpMethod.Should().BeNull();
        tool.HttpPath.Should().BeNull();
    }

    [Fact]
    public async Task ServerDefinitionRepository_ListApprovedOnlyReturnsApproved()
    {
        var repo = CreateServerRepo();
        var pending = new McpServerDefinition
        {
            Name = "pending-server",
            DisplayName = "Pending",
            SpecContent = "{}",
            SpecHash = "hash1",
            BaseUrl = "https://pending.example.com",
            ApprovalStatus = "pending"
        };
        var approved = new McpServerDefinition
        {
            Name = "approved-server",
            DisplayName = "Approved",
            SpecContent = "{}",
            SpecHash = "hash2",
            BaseUrl = "https://approved.example.com",
            ApprovalStatus = "approved"
        };

        await repo.AddAsync(pending);
        await repo.AddAsync(approved);

        var approvedList = await repo.ListApprovedAsync();

        approvedList.Should().ContainSingle(s => s.Name == "approved-server");
    }

    [Fact]
    public async Task GatewayApiKeyRepository_GetByPrefixFiltersRevoked()
    {
        var serverRepo = CreateServerRepo();
        var server = await serverRepo.AddAsync(new McpServerDefinition
        {
            Name = "key-test",
            DisplayName = "Key Test",
            SpecContent = "{}",
            SpecHash = "hash",
            BaseUrl = "https://key.example.com"
        });

        var repo = CreateApiKeyRepo();
        var key = new GatewayApiKey
        {
            ServerDefinitionId = server.Id,
            KeyHash = "bcrypt-hash-abc",
            KeyPrefix = "mgk_abc1",
            Name = "Test Key",
            Scopes = ["key-test"]
        };

        var added = await repo.AddAsync(key);
        var retrieved = await repo.GetByPrefixAsync("mgk_abc1");

        retrieved.Should().NotBeNull();
        retrieved!.KeyPrefix.Should().Be("mgk_abc1");

        await repo.RevokeAsync(added.Id);
        var afterRevoke = await repo.GetByPrefixAsync("mgk_abc1");

        afterRevoke.Should().BeNull();
    }

    [Fact]
    public async Task ToolOverrideRepository_UpsertAndRetrieve()
    {
        var serverRepo = CreateServerRepo();
        var server = await serverRepo.AddAsync(new McpServerDefinition
        {
            Name = "override-test",
            DisplayName = "Override Test",
            SpecContent = "{}",
            SpecHash = "hash",
            BaseUrl = "https://override.example.com"
        });

        var repo = CreateOverrideRepo();
        var overrideEntry = new ToolOverride
        {
            ServerDefinitionId = server.Id,
            ToolName = "get_items",
            DescriptionOverride = "Custom description",
            Visible = false
        };

        var added = await repo.UpsertAsync(overrideEntry);
        var retrieved = await repo.GetAsync(server.Id, "get_items");

        retrieved.Should().NotBeNull();
        retrieved!.DescriptionOverride.Should().Be("Custom description");
        retrieved.Visible.Should().BeFalse();

        var updated = await repo.UpsertAsync(new ToolOverride
        {
            ServerDefinitionId = server.Id,
            ToolName = "get_items",
            DescriptionOverride = "Updated description",
            Visible = true
        });

        updated.DescriptionOverride.Should().Be("Updated description");
        updated.Visible.Should().BeTrue();
    }
}
