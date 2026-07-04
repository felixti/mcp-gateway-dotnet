using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace McpGateway.Core.Health;

public static class CoreHealthServiceExtensions
{
    public static IServiceCollection AddMcpHealth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<HealthOptions>(configuration.GetSection(HealthOptions.SectionName));
        services.AddSingleton<IReadinessState, ReadinessState>();
        services.AddSingleton<IInFlightCallTracker, InFlightCallTracker>();

        services.AddSingleton<IDependencyProbe>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthOptions>>().Value;
            return new PostgresDependencyProbe(
                sp.GetRequiredService<IConfiguration>(),
                options.PostgresConnectionName);
        });

        services.AddSingleton<IDependencyProbe>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthOptions>>().Value;
            return new StorageQueueDependencyProbe(
                sp.GetRequiredService<IConfiguration>(),
                options.StorageQueueConnectionName);
        });

        services.AddSingleton<IDependencyProbe, ToolStoreDependencyProbe>();
        services.AddSingleton<DependencyHealthChecker>();

        services.TryAddSingleton<IAuditFlusher, NullAuditFlusher>();
        services.TryAddSingleton<IDiskFallbackFlusher, NullDiskFallbackFlusher>();
        services.AddHostedService<DependencyHealthChecker>();
        services.AddHostedService<GracefulShutdownService>();

        return services;
    }

    private sealed class NullAuditFlusher : IAuditFlusher
    {
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullDiskFallbackFlusher : IDiskFallbackFlusher
    {
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
