using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Api;
using McpGateway.Management.Contracts;
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
using Xunit.Abstractions;

namespace McpGateway.IntegrationTests;

[CollectionDefinition("Smoke")]
public sealed class SmokeCollection : ICollectionFixture<SmokeTestFactory> { }

public sealed class SmokeTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("mcp_smoke_tests")
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
                .AddScheme<AuthenticationSchemeOptions, SmokeAllowAnyAuthHandler>("TestMcp", _ => { });

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
    }

    public new async Task DisposeAsync()
    {
        await _pg.DisposeAsync();
    }
}

internal sealed class SmokeAllowAnyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public SmokeAllowAnyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
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

[Collection("Smoke")]
public class SmokeTests : IClassFixture<SmokeTestFactory>
{
    private const string DevAdminHeader = "X-Dev-Admin";
    private const string AdminUpn = "smoke@corp.local";
    private const string PetstoreSpecUrl = "https://petstore3.swagger.io/api/v3/openapi.json";
    private const string PetstoreBaseUrl = "https://petstore3.swagger.io/api/v3";

    private readonly SmokeTestFactory _factory;
    private readonly ITestOutputHelper _output;

    public SmokeTests(SmokeTestFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAdminHeader, AdminUpn);
        return client;
    }

    private HttpClient CreateMcpClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client;
    }

    private static StringContent JsonBody(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadFirstEventData(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..];
                return JsonDocument.Parse(json).RootElement.Clone();
            }
        }

        throw new InvalidOperationException("MCP response had no 'data:' line");
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task PublicOpenApiSpec_EndToEnd_ToolCallReturnsUpstreamResponse()
    {
        string specContent;
        using (var http = new HttpClient())
        {
            specContent = await http.GetStringAsync(PetstoreSpecUrl);
        }

        specContent.Should().Contain("openapi");

        var adminClient = CreateAdminClient();
        var create = new CreateServerRequest(
            "petstore-smoke",
            "Petstore Smoke",
            "Swagger Petstore v3 smoke test",
            null,
            specContent,
            PetstoreBaseUrl,
            "passthrough",
            new JsonObject(),
            "all",
            "universal",
            1440,
            AdminUpn);

        var register = await adminClient.PostAsync("/admin/servers", JsonBody(create));
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var approve = await adminClient.PostAsync("/admin/servers/petstore-smoke/approve", content: null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);

        var mcpClient = CreateMcpClient();
        var listResponse = await mcpClient.PostAsync(
            "/mcp/petstore-smoke",
            JsonBody(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/list",
                @params = new { }
            }));
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await ReadFirstEventData(listResponse);
        var tools = listBody.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().BeGreaterThan(0);

        var callResponse = await mcpClient.PostAsync(
            "/mcp/petstore-smoke",
            JsonBody(new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new
                {
                    name = "findpetsbystatus",
                    arguments = new { status = "available" }
                }
            }));
        callResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var callBody = await ReadFirstEventData(callResponse);
        var result = callBody.GetProperty("result");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        _output.WriteLine($"Tool call result text: {text}");
        result.GetProperty("isError").GetBoolean().Should().BeFalse();
        text.Should().Contain("available");
    }
}
