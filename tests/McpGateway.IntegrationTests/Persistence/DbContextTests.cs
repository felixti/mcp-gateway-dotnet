using FluentAssertions;
using McpGateway.Persistence;
using McpGateway.Persistence.Entities;

namespace McpGateway.IntegrationTests.Persistence;

[Collection("Persistence")]
public class DbContextTests
{
    private readonly PostgreSqlFixture _fixture;

    public DbContextTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanInsertAndRetrieveServerDefinition()
    {
        await using var context = _fixture.CreateDbContext();
        var server = new McpServerDefinitionEntity
        {
            Name = "test-api",
            DisplayName = "Test API",
            SpecContent = "{}",
            SpecHash = "abc123",
            BaseUrl = "https://test.example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.ServerDefinitions.Add(server);
        await context.SaveChangesAsync();

        await using var readContext = _fixture.CreateDbContext();
        var retrieved = await readContext.ServerDefinitions.FindAsync(server.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("test-api");
        retrieved.DisplayName.Should().Be("Test API");
    }

    [Fact]
    public async Task ServerNameIsUnique()
    {
        await using var context = _fixture.CreateDbContext();
        var first = new McpServerDefinitionEntity
        {
            Name = "unique-api",
            DisplayName = "First",
            SpecContent = "{}",
            SpecHash = "hash1",
            BaseUrl = "https://first.example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var duplicate = new McpServerDefinitionEntity
        {
            Name = "unique-api",
            DisplayName = "Second",
            SpecContent = "{}",
            SpecHash = "hash2",
            BaseUrl = "https://second.example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.ServerDefinitions.Add(first);
        await context.SaveChangesAsync();

        context.ServerDefinitions.Add(duplicate);

        await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingServerDefinitionCascadesToTools()
    {
        await using var context = _fixture.CreateDbContext();
        var server = new McpServerDefinitionEntity
        {
            Name = "cascade-api",
            DisplayName = "Cascade API",
            SpecContent = "{}",
            SpecHash = "hash",
            BaseUrl = "https://cascade.example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var tool = new ToolEntity
        {
            ToolName = "get_things",
            Description = "Get things",
            HttpMethod = "GET",
            HttpPath = "/things",
            InputSchema = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        server.Tools.Add(tool);
        context.ServerDefinitions.Add(server);
        await context.SaveChangesAsync();

        context.ServerDefinitions.Remove(server);
        await context.SaveChangesAsync();

        await using var readContext = _fixture.CreateDbContext();
        var remainingTools = readContext.Tools.Where(t => t.ToolName == "get_things").ToList();
        remainingTools.Should().BeEmpty();
    }
}
