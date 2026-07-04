using McpGateway.Core.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace McpGateway.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly DateTime ProcessStartedAt = DateTime.UtcNow;

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", HandleLiveness)
            .AllowAnonymous()
            .WithName("Liveness");

        app.MapGet("/ready", HandleReadiness)
            .AllowAnonymous()
            .WithName("Readiness");
    }

    private static IResult HandleLiveness()
    {
        var uptime = DateTime.UtcNow - ProcessStartedAt;
        return Results.Json(new
        {
            status = "ok",
            uptime_seconds = (long)uptime.TotalSeconds
        });
    }

    private static IResult HandleReadiness(IReadinessState readinessState)
    {
        var snapshot = readinessState.Current;
        var response = new
        {
            status = snapshot.IsReady ? "ready" : "not_ready",
            checks = new
            {
                postgres = snapshot.PostgresOk ? "ok" : "fail",
                storage_queue = snapshot.StorageQueueOk ? "ok" : "fail",
                tool_store = snapshot.ToolStoreOk ? "ok" : "fail"
            },
            errors = new
            {
                postgres = snapshot.PostgresError,
                storage_queue = snapshot.StorageQueueError,
                tool_store = snapshot.ToolStoreError
            },
            last_checked_at = snapshot.LastCheckedAt
        };

        return snapshot.IsReady
            ? Results.Json(response, statusCode: StatusCodes.Status200OK)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
