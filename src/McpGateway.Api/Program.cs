using McpGateway.Api;
using McpGateway.Api.Auth;
using McpGateway.Api.Endpoints;
using McpGateway.Core;
using McpGateway.Core.Audit;
using McpGateway.Core.Health;
using McpGateway.Core.Proxy;
using McpGateway.Management.Services;
using McpGateway.McpSdk;
using McpGateway.Persistence;
using McpGateway.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddMcpPersistence(builder.Configuration);
builder.Services.AddMcpTelemetry(builder.Configuration);
builder.Services.AddMcpAudit(builder.Configuration);
builder.Services.AddMcpProxy();
builder.Services.AddMcpCore();
builder.Services.AddMcpHealth(builder.Configuration);
builder.Services.AddMcpManagement();
builder.Services.AddMcpGatewayMcp();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddAuthentication()
    .AddScheme<JwtAuthOptions, JwtAuthHandler>(JwtAuthHandler.SchemeName, options =>
    {
        options.TenantId = builder.Configuration["EntraId:TenantId"] ?? string.Empty;
        options.Audience = builder.Configuration["EntraId:Audience"] ?? string.Empty;
        options.JwksUri = builder.Configuration["EntraId:JwksUri"] ?? string.Empty;
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>(ApiKeyAuthHandler.SchemeName, _ => { })
    .AddScheme<AdminJwtOptions, AdminAuthHandler>(AdminAuthHandler.SchemeName, _ => { })
    .AddScheme<DevelopmentAdminOptions, DevelopmentAdminAuthHandler>(DevelopmentAdminAuthHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("McpClient", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddAuthenticationSchemes(JwtAuthHandler.SchemeName, ApiKeyAuthHandler.SchemeName);
    });

    options.AddPolicy("Admin", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddAuthenticationSchemes(AdminAuthHandler.SchemeName, DevelopmentAdminAuthHandler.SchemeName);
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<McpGateway.Persistence.McpGatewayDbContext>(
        name: "ef_core_dbcontext",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" });

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
});

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(35);
    options.ServicesStartConcurrently = false;
    options.ServicesStopConcurrently = false;
});

var app = builder.Build();

app.UseAdminExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapMcp();
app.MapAdminEndpoints();

app.Run();

public partial class Program { }
