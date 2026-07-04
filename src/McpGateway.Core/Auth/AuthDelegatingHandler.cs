using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Auth;

public class AuthDelegatingHandler : DelegatingHandler
{
    private readonly AuthStrategyResolver _authStrategyResolver;
    private readonly IServiceProvider _serviceProvider;

    public AuthDelegatingHandler(AuthStrategyResolver authStrategyResolver, IServiceProvider serviceProvider)
    {
        _authStrategyResolver = authStrategyResolver;
        _serviceProvider = serviceProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = _serviceProvider.GetService(typeof(ToolCallContext)) as ToolCallContext;
        if (context is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var header = await _authStrategyResolver.ResolveAuthorizationHeaderAsync(
            GetServerDefinition(context.ServerName),
            context.Caller,
            cancellationToken);

        if (!string.IsNullOrEmpty(header))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", header["Bearer ".Length..]);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static McpServerDefinition GetServerDefinition(string serverName)
    {
        throw new NotImplementedException("Server definition resolution is wired by the caller.");
    }
}
