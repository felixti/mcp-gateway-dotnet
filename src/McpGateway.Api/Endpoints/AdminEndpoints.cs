using System.Text.Json;
using FluentValidation;
using McpGateway.Api.Auth;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Management.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ValidationException = McpGateway.Management.Exceptions.ValidationException;

namespace McpGateway.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin")
            .RequireAuthorization("Admin")
            .WithTags("admin");

        group.MapGet("/servers", async (ServerManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)));

        group.MapGet("/servers/{name}", async (string name, ServerManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(name, ct)));

        group.MapPost("/servers/validate", async (
            CreateServerRequest body,
            IValidator<CreateServerRequest> validator,
            ServerManagementService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            return Results.Ok(await svc.ValidateAsync(body, ct));
        });

        group.MapPost("/servers", async (
            CreateServerRequest body,
            IValidator<CreateServerRequest> validator,
            ServerManagementService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            return Results.Created($"/admin/servers/{body.Name}", await svc.RegisterAsync(body, ct));
        });

        group.MapPatch("/servers/{name}", async (
            string name,
            UpdateServerRequest body,
            IValidator<UpdateServerRequest> validator,
            ServerManagementService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            return Results.Ok(await svc.UpdateAsync(name, body, ct));
        });

        group.MapDelete("/servers/{name}", async (string name, ServerManagementService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(name, ct);
            return Results.NoContent();
        });

        group.MapPost("/servers/{name}/refresh", async (string name, ServerManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.RefreshAsync(name, ct)));

        group.MapPost("/servers/{name}/approve", async (string name, ServerManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.ApproveAsync(name, ct)));

        group.MapGet("/servers/{name}/tools", async (string name, ToolManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(name, ct)));

        group.MapPatch("/servers/{name}/tools/{toolName}", async (
            string name,
            string toolName,
            UpdateToolRequest body,
            IValidator<UpdateToolRequest> validator,
            ToolManagementService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            await svc.UpdateAsync(name, toolName, body, ct);
            return Results.NoContent();
        });

        group.MapPut("/servers/{name}/tools/{toolName}/override", async (
            string name,
            string toolName,
            PutOverrideRequest body,
            IValidator<PutOverrideRequest> validator,
            ToolManagementService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            await svc.PutOverrideAsync(name, toolName, body, ct);
            return Results.NoContent();
        });

        group.MapDelete("/servers/{name}/tools/{toolName}/override", async (
            string name,
            string toolName,
            ToolManagementService svc,
            CancellationToken ct) =>
        {
            await svc.DeleteOverrideAsync(name, toolName, ct);
            return Results.NoContent();
        });

        group.MapGet("/servers/{name}/api-keys", async (string name, GatewayApiKeyService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(name, ct)));

        group.MapPost("/servers/{name}/api-keys", async (
            string name,
            CreateApiKeyRequest body,
            IValidator<CreateApiKeyRequest> validator,
            GatewayApiKeyService svc,
            CancellationToken ct) =>
        {
            await ValidateAsync(validator, body);
            return Results.Created($"/admin/servers/{name}/api-keys/{Guid.NewGuid()}", await svc.IssueAsync(name, body, ct));
        });

        group.MapDelete("/servers/{name}/api-keys/{keyId:guid}", async (
            string name,
            Guid keyId,
            GatewayApiKeyService svc,
            CancellationToken ct) =>
        {
            await svc.RevokeAsync(name, keyId, ct);
            return Results.NoContent();
        });

        group.MapPost("/servers/{name}/spec", async (
            string name,
            SpecUploadRequest body,
            ServerManagementService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.UploadSpecAsync(name, body, ct)));

        group.MapPut("/servers/{name}/spec-source", async (
            string name,
            SpecSourceUpdateRequest body,
            ServerManagementService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.UpdateSpecSourceAsync(name, body, ct)));

        group.MapGet("/servers/{name}/spec", async (string name, ServerManagementService svc, CancellationToken ct) =>
        {
            var (content, hash) = await svc.GetSpecAsync(name, ct);
            return Results.Ok(new { content, hash });
        });

        group.MapGet("/servers/{name}/spec/diff/{versionId:guid}", async (
            string name,
            Guid versionId,
            ServerManagementService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.GetSpecDiffAsync(name, versionId, ct)));

        return app;
    }

    private static async Task ValidateAsync<T>(IValidator<T> validator, T instance)
    {
        var result = await validator.ValidateAsync(instance);
        if (!result.IsValid)
            throw new ValidationException(result.Errors);
    }
}

public static class AdminExceptionMiddleware
{
    public static IApplicationBuilder UseAdminExceptionHandler(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            try
            {
                await next();
            }
            catch (NotFoundException ex)
            {
                await Write(ctx, StatusCodes.Status404NotFound, "not_found", ex.Message);
            }
            catch (ConflictException ex)
            {
                await Write(ctx, StatusCodes.Status409Conflict, "conflict", ex.Message);
            }
            catch (ValidationException ex)
            {
                var errors = ex.Errors
                    .Select(e => new { field = e.PropertyName, message = e.ErrorMessage })
                    .ToArray();
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = "validation_failed", errors }));
            }
            catch (OpenApiSpecValidationException ex)
            {
                var issues = ex.Report.Errors
                    .Concat(ex.Report.Warnings)
                    .Select(i => new { pointer = i.Pointer, code = i.Code, message = i.Message, severity = i.Severity })
                    .ToArray();
                ctx.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = "openapi_spec_invalid", issues }));
            }
            catch (Exception ex) when (ctx.RequestAborted.IsCancellationRequested == false)
            {
                var logger = ctx.RequestServices.GetRequiredService<ILogger<MarkerClass>>();
                logger.LogError(ex, "Unhandled admin API error.");
                await Write(ctx, StatusCodes.Status500InternalServerError, "internal_error", ex.Message);
            }
        });
    }

    private static async Task Write(HttpContext ctx, int status, string error, string detail)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new ErrorResponse(error, detail)));
    }

    private sealed class MarkerClass { }
}
