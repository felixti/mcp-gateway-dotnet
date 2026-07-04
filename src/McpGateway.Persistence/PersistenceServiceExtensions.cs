using EFCore.NamingConventions;
using McpGateway.Core.Repositories;
using McpGateway.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace McpGateway.Persistence;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddMcpPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<McpGatewayDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("PostgreSql")
                ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
            options
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IServerDefinitionRepository, ServerDefinitionRepository>();
        services.AddScoped<IGatewayApiKeyRepository, GatewayApiKeyRepository>();
        services.AddScoped<IToolOverrideRepository, ToolOverrideRepository>();
        services.AddScoped<ISpecVersionRepository, SpecVersionRepository>();

        return services;
    }
}
