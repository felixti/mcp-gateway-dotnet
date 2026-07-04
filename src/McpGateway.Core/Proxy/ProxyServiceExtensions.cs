using McpGateway.Core.Audit;
using McpGateway.Core.Auth;
using McpGateway.Core.Health;
using McpGateway.Core.ToolStore;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace McpGateway.Core.Proxy;

public static class ProxyServiceExtensions
{
    public static IServiceCollection AddMcpProxy(this IServiceCollection services)
    {
        services.AddSingleton<OboTokenCache>();
        services.AddScoped<AuthStrategyResolver>();
        services.AddHttpClient<IOboTokenExchange, OboTokenExchange>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler());

        services.AddSingleton<HttpRequestBuilder>();
        services.AddSingleton<ResponseWrapper>();
        services.AddSingleton<MetaToolsHandler>();
        services.AddSingleton<ToolCallContextAccessor>();

        services.AddHttpClient(ToolCallHandler.HttpClientName)
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        services.AddScoped<ToolCallHandler>(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            return new ToolCallHandler(
                provider.GetRequiredService<IToolStore>(),
                provider.GetRequiredService<HttpRequestBuilder>(),
                provider.GetRequiredService<ResponseWrapper>(),
                factory.CreateClient(ToolCallHandler.HttpClientName),
                provider.GetRequiredService<IAuditEmitter>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ToolCallContextAccessor>(),
                provider.GetRequiredService<IInFlightCallTracker>());
        });

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
    }
}
