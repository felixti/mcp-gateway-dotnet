namespace McpGateway.Core.Auth;

public interface IOboTokenExchange
{
    Task<string> ExchangeAsync(string callerToken, string resource, CancellationToken ct = default);
}
