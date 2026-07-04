using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using McpGateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace McpGateway.IntegrationTests;

public sealed class McpApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("mcp_endpoint_tests")
        .WithUsername("mcp")
        .WithPassword("mcp")
        .Build();

    public string ConnectionString => _pg.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = ConnectionString,
                ["Admin:UseDevHandler"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<McpGatewayDbContext>));
            services.AddDbContext<McpGatewayDbContext>(o => o.UseNpgsql(ConnectionString));

            services.RemoveAll<IHostedService>();

            services.AddAuthentication("TestMcp")
                .AddScheme<AuthenticationSchemeOptions, AllowAnyAuthHandler>("TestMcp", _ => { });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("McpClient", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddAuthenticationSchemes("TestMcp");
                });
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<McpGatewayDbContext>();
        await db.Database.MigrateAsync();
        var store = scope.ServiceProvider.GetRequiredService<IToolStore>();
        SeedStore(store);
    }

    public new async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
    }

    public static void SeedStore(IToolStore store)
    {
        store.AddServer(new McpServerDefinition
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "approved",
            DisplayName = "Approved Server",
            Description = "Approved for MCP exposure.",
            SpecContent = "{}",
            SpecHash = "hash-approved",
            BaseUrl = "https://approved.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}",
            ToolMode = ToolMode.All,
            ClientProfile = ClientProfile.Universal,
            PollIntervalMinutes = 1440,
            Status = "active",
            ApprovalStatus = "approved",
            ApprovedAt = DateTime.UtcNow,
            ApprovedBy = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Tools = new List<ToolDefinition>
            {
                new()
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    ToolName = "greet",
                    Description = "Returns a greeting.",
                    HttpMethod = "GET",
                    HttpPath = "/greet",
                    InputSchema = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""",
                    AuthConfig = "{}",
                    Visible = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
                new()
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    ToolName = "hidden-tool",
                    Description = "Not exposed.",
                    HttpMethod = "GET",
                    HttpPath = "/hidden",
                    InputSchema = """{"type":"object","properties":{}}""",
                    AuthConfig = "{}",
                    Visible = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
            },
        });

        store.AddServer(new McpServerDefinition
        {
            Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Name = "pending",
            DisplayName = "Pending Server",
            Description = "Not approved yet.",
            SpecContent = "{}",
            SpecHash = "hash-pending",
            BaseUrl = "https://pending.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}",
            ToolMode = ToolMode.All,
            ClientProfile = ClientProfile.Universal,
            PollIntervalMinutes = 1440,
            Status = "active",
            ApprovalStatus = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Tools = new List<ToolDefinition>
            {
                new()
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    ToolName = "should-not-appear",
                    Description = "Pending tool.",
                    HttpMethod = "GET",
                    HttpPath = "/pending",
                    InputSchema = """{"type":"object","properties":{}}""",
                    AuthConfig = "{}",
                    Visible = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
            },
        });
    }
}

internal sealed class AllowAnyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public AllowAnyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new System.Security.Claims.ClaimsIdentity("TestMcp");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestMcp");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class McpEndpointTests : IClassFixture<McpApiFactory>
{
    private readonly McpApiFactory _factory;

    public McpEndpointTests(McpApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client;
    }

    private static StringContent JsonBody(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadFirstEventData(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        string? line = await reader.ReadLineAsync();
        while (line is not null)
        {
            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..];
                return JsonDocument.Parse(json).RootElement.Clone();
            }
            line = await reader.ReadLineAsync();
        }
        throw new InvalidOperationException("MCP response had no 'data:' line");
    }

    [Fact]
    public async Task Initialize_Handshake_ReturnsServerInfo()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/mcp/approved", JsonBody(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "test", version = "0.0.0" }
            }
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadFirstEventData(response);
        body.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString()
            .Should().Be("mcp-gateway");
    }

    [Fact]
    public async Task ToolsList_ApprovedServer_ReturnsVisibleToolsOnly()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/mcp/approved", JsonBody(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list",
            @params = new { }
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadFirstEventData(response);
        var tools = body.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);
        tools[0].GetProperty("name").GetString().Should().Be("greet");
    }

    [Fact]
    public async Task ToolsList_UnknownServer_ReturnsEmptyList()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/mcp/does-not-exist", JsonBody(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/list",
            @params = new { }
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadFirstEventData(response);
        body.GetProperty("result").GetProperty("tools").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ToolsCall_UnapprovedServer_ReturnsServerNotApprovedError()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/mcp/pending", JsonBody(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "tools/call",
            @params = new { name = "should-not-appear", arguments = new { } }
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadFirstEventData(response);
        var result = body.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().Contain("-32005").And.Contain("not approved");
    }

    [Fact]
    public async Task ToolsCall_UnknownToolOnApprovedServer_ReturnsToolNotFound()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/mcp/approved", JsonBody(new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "tools/call",
            @params = new { name = "nope", arguments = new { } }
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadFirstEventData(response);
        var result = body.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("content")[0].GetProperty("text").GetString()
            .Should().Contain("nope").And.Contain("not found");
    }

    [Fact]
    public async Task ToolsList_AfterStoreMutation_PicksUpChanges()
    {
        var client = CreateClient();
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IToolStore>();
        store.AddServer(new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "mutated",
            DisplayName = "Mutated Server",
            Description = "Server added at runtime.",
            SpecContent = "{}",
            SpecHash = "hash-mutated",
            BaseUrl = "https://mutated.example.com",
            AuthStrategy = "obo",
            AuthConfig = "{}",
            ToolMode = ToolMode.All,
            ClientProfile = ClientProfile.Universal,
            PollIntervalMinutes = 1440,
            Status = "active",
            ApprovalStatus = "approved",
            ApprovedAt = DateTime.UtcNow,
            ApprovedBy = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Tools = new List<ToolDefinition>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ToolName = "fresh-tool",
                    Description = "Added at runtime.",
                    HttpMethod = "GET",
                    HttpPath = "/fresh",
                    InputSchema = """{"type":"object","properties":{}}""",
                    AuthConfig = "{}",
                    Visible = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
            },
        });

        var response = await client.PostAsync("/mcp/mutated", JsonBody(new
        {
            jsonrpc = "2.0",
            id = 6,
            method = "tools/list",
            @params = new { }
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadFirstEventData(response);
        var tools = body.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);
        tools[0].GetProperty("name").GetString().Should().Be("fresh-tool");
    }
}
