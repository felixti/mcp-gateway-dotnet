using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Persistence.Repositories;

namespace McpGateway.IntegrationTests.SpecManagement;

[Collection("Persistence")]
public class SpecVersionRepositoryTests
{
    private readonly PostgreSqlFixture _fixture;

    public SpecVersionRepositoryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_PersistsVersionWithHashAndContent()
    {
        var serverRepo = new ServerDefinitionRepository(_fixture.CreateDbContext());
        var server = await serverRepo.AddAsync(new McpServerDefinition
        {
            Name = "spec-version-test",
            DisplayName = "Spec Version Test",
            SpecContent = "{}",
            SpecHash = "original-hash",
            BaseUrl = "https://spec-version.example.com"
        });

        await using var context = _fixture.CreateDbContext();
        var repo = new SpecVersionRepository(context);

        var version = new SpecVersion
        {
            ServerDefinitionId = server.Id,
            SpecHash = "new-hash",
            SpecContent = "{\"openapi\":\"3.0.0\"}",
            ToolCount = 5,
            DiffSummary = "{\"added\":[\"foo\"],\"removed\":[],\"changed\":[]}"
        };

        var added = await repo.AddAsync(version);

        added.Id.Should().NotBe(Guid.Empty);
        added.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        var retrieved = await repo.GetAsync(added.Id);
        retrieved.Should().NotBeNull();
        retrieved!.SpecHash.Should().Be("new-hash");
        retrieved.ToolCount.Should().Be(5);
    }
}
