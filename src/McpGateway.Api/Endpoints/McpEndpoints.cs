using McpGateway.McpSdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace McpGateway.Api.Endpoints;

public static class McpEndpoints
{
    public static IEndpointConventionBuilder MapMcp(this IEndpointRouteBuilder endpoints)
        => endpoints.MapMcpGateway().RequireAuthorization("McpClient");
}
