using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace McpGateway.Core.Audit;

public static class AuditServiceExtensions
{
    public static IServiceCollection AddMcpAudit(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QueueEmitterOptions>(opts =>
        {
            opts.ConnectionString = configuration["AzureStorage:ConnectionString"]
                ?? throw new InvalidOperationException("AzureStorage:ConnectionString is required.");
            opts.QueueName = configuration["AzureStorage:Audit:QueueName"] ?? "mcp-audit";
        });

        services.Configure<DiskFallbackOptions>(opts =>
        {
            var directory = configuration["Audit:DiskFallback:Directory"];
            if (!string.IsNullOrWhiteSpace(directory))
            {
                opts.Directory = directory;
            }
        });

        services.AddSingleton<DiskFallback>();
        services.AddSingleton<IAuditEmitter, QueueEmitter>();
        services.AddHostedService<DiskFallbackRetryWorker>();

        return services;
    }
}
