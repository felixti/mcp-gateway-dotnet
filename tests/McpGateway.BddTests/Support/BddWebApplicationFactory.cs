using EFCore.NamingConventions;
using McpGateway.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpGateway.BddTests.Support;

public sealed class BddWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _postgresConnectionString;
    private readonly string _azuriteConnectionString;

    public BddWebApplicationFactory(string postgresConnectionString, string azuriteConnectionString)
    {
        _postgresConnectionString = postgresConnectionString;
        _azuriteConnectionString = azuriteConnectionString;
    }

    public string ConnectionString => _postgresConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = _postgresConnectionString,
                ["ConnectionStrings:StorageQueue"] = _azuriteConnectionString,
                ["AzureStorage:ConnectionString"] = _azuriteConnectionString,
                ["AzureStorage:Audit:QueueName"] = "mcp-audit-events",
                ["Otel:Exporter:Otlp:Endpoint"] = "",
                ["Admin:UseDevHandler"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthenticationSchemeProvider>();
            services.AddAuthentication("BddMcp")
                .AddScheme<AuthenticationSchemeOptions, AllowAnyAuthHandler>("BddMcp", _ => { });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("McpClient", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddAuthenticationSchemes("BddMcp");
                });
                options.AddPolicy("Admin", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddAuthenticationSchemes("Admin", "DevAdmin", "BddMcp");
                });
            });

            services.RemoveAll<IHostedService>();
        });
    }

    public McpGatewayDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<McpGatewayDbContext>()
            .UseNpgsql(_postgresConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new McpGatewayDbContext(options);
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
        var identity = new System.Security.Claims.ClaimsIdentity("BddMcp");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "BddMcp");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
