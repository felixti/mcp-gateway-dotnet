using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using Microsoft.Extensions.DependencyInjection;

namespace McpGateway.Core;

public static class CoreServiceExtensions
{
    public static IServiceCollection AddMcpCore(this IServiceCollection services)
    {
        services.AddSingleton<IToolStore, InMemoryToolStore>();
        services.AddScoped<ToolStoreInitializer>();

        services.AddHttpClient(SpecFetcher.HttpClientName);
        services.AddSingleton<ISpecFetcher>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new SpecFetcher(factory);
        });
        services.AddSingleton<IToolGenerator, ToolGenerator>();
        services.AddSingleton<IOpenApiSpecValidator, OpenApiSpecValidator>();
        services.AddSingleton<ISpecDiffService, SpecDiffService>();
        services.AddScoped<ServerSpecRefresher>();
        services.AddScoped<ISpecRefresher, SpecRefresher>();

        services.AddHostedService<SpecRefresherBackgroundService>();

        return services;
    }
}
