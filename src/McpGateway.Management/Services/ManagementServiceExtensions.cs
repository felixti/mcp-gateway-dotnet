using FluentValidation;
using McpGateway.Management.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace McpGateway.Management.Services;

public static class ManagementServiceExtensions
{
    public static IServiceCollection AddMcpManagement(this IServiceCollection services)
    {
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICallerIdentityAccessor, CallerIdentityAccessor>();

        services.AddScoped<ServerManagementService>();
        services.AddScoped<ToolManagementService>();
        services.AddScoped<GatewayApiKeyService>();
        services.AddScoped<ClientProfileService>();

        services.AddValidatorsFromAssemblyContaining<Contracts.CreateServerRequest>(ServiceLifetime.Scoped, includeInternalTypes: true);

        return services;
    }
}
