using EFCore.NamingConventions;
using McpGateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace McpGateway.IntegrationTests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public PostgreSqlFixture()
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("mcp_gateway_tests")
            .WithUsername("mcp")
            .WithPassword("mcp")
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public McpGatewayDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<McpGatewayDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new McpGatewayDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
