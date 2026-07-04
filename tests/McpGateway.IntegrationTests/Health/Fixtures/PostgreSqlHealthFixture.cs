using Testcontainers.PostgreSql;

namespace McpGateway.IntegrationTests.Health.Fixtures;

public sealed class PostgreSqlHealthFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("mcp_health_tests")
        .WithUsername("mcp")
        .WithPassword("mcp")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();
    public async Task DisposeAsync() => await _container.DisposeAsync();
}
