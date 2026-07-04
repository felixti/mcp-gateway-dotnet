using McpGateway.IntegrationTests.Health.Fixtures;

namespace McpGateway.IntegrationTests.Health;

[CollectionDefinition("Health")]
public class HealthFixtureCollection : ICollectionFixture<PostgreSqlHealthFixture>, ICollectionFixture<AzuriteHealthFixture>
{
}
