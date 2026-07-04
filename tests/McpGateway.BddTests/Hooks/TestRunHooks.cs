using McpGateway.BddTests.Steps;
using McpGateway.BddTests.Support;
using Microsoft.EntityFrameworkCore;
using Reqnroll;
using Reqnroll.BoDi;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;
using WireMock.Server;
using WireMock.Settings;

namespace McpGateway.BddTests.Hooks;

[Binding]
public sealed class TestRunHooks
{
    private static readonly PostgreSqlContainer Postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("mcp_gateway_bdd")
        .WithUsername("mcp")
        .WithPassword("mcp")
        .Build();

    private static readonly AzuriteContainer Azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.34.0")
        .Build();

    private static bool _infrastructureStarted;
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    private readonly IObjectContainer _objectContainer;

    public TestRunHooks(IObjectContainer objectContainer)
    {
        _objectContainer = objectContainer;
    }

    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        await InitLock.WaitAsync();
        try
        {
            if (_infrastructureStarted) return;
            await Postgres.StartAsync();
            await Azurite.StartAsync();
            _infrastructureStarted = true;
        }
        finally
        {
            InitLock.Release();
        }
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        await Postgres.DisposeAsync();
        await Azurite.DisposeAsync();
    }

    [BeforeScenario]
    public async Task BeforeScenario()
    {
        var context = new TestContext();
        _objectContainer.RegisterInstanceAs(context);

        var entraId = new EntraIdMock(tenantId: "test-tenant", clientId: "mcp-gateway-app");
        var upstreamApi = WireMockServer.Start(new WireMockServerSettings
        {
            Urls = new[] { "http://127.0.0.1:0" }
        });

        var factory = new BddWebApplicationFactory(
            postgresConnectionString: Postgres.GetConnectionString(),
            azuriteConnectionString: Azurite.GetConnectionString());

        await ApplyMigrationsAsync(factory);

        context.EntraId = entraId;
        context.UpstreamApi = upstreamApi;
        context.Factory = factory;
    }

    [AfterScenario]
    public async Task AfterScenario()
    {
        var context = _objectContainer.Resolve<TestContext>();
        context.UpstreamApi?.Stop();
        if (context.EntraId is not null)
        {
            await context.EntraId.DisposeAsync();
        }
        context.Factory?.Dispose();
    }

    private static async Task ApplyMigrationsAsync(BddWebApplicationFactory factory)
    {
        await using var context = factory.CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }
}
