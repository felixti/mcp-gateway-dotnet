# `McpGateway.Management` — Agent Context

## Scope

Admin-use-case business logic. This project sits between `McpGateway.Api` (HTTP) and `McpGateway.Core` + `McpGateway.Persistence` (data). It owns the request/response contracts, validation, and the services that implement `/admin/servers/*` operations.

It does **not** depend directly on `Microsoft.AspNetCore.*` HTTP types except via the shared framework reference for `IHttpContextAccessor`.

## Extension Entrypoint

`Services/ManagementServiceExtensions.cs`:
- `AddMcpManagement()` registers `ServerManagementService`, `ToolManagementService`, `GatewayApiKeyService`, `ClientProfileService` as **scoped**.
- Registers `IHttpContextAccessor` and `ICallerIdentityAccessor`.
- Adds FluentValidation validators from this assembly.

## Key Services

| Service | Responsibility |
|---|---|
| `ServerManagementService` | Register, get, list, update, delete (soft), refresh, approve, upload spec, spec source, spec diff |
| `ToolManagementService` | List/update tools and tool overrides |
| `GatewayApiKeyService` | Issue/revoke/list API keys; BCrypt-hashes secrets |
| `ClientProfileService` | Per-server client-profile override |

## Contracts & Validation

- `Contracts/Dtos.cs` — request/response records; `ServerResponse.FromDomain(...)` is the canonical mapper.
- `Contracts/Validators.cs` — FluentValidation validators for each request record.
- `Exceptions/NotFoundException.cs` — maps to HTTP 404.
- `Exceptions/ConflictException.cs` — maps to HTTP 409.
- `Exceptions/ValidationException.cs` — maps to HTTP 400.

## Admin Identity

- `Auth/ICallerIdentityAccessor.cs` / `CallerIdentityAccessor.cs`
- Reads `mcp-gateway-admin` role and `admin_upn` claim from `HttpContext.User`.
- Throws if an admin-only operation is invoked by a non-admin.

## Tool Store Ownership

This is the **only writer to `IToolStore`** in the codebase:
- `AddServer` on approve.
- `RemoveServer` on update/refresh/delete.

`McpGateway.Core` and `McpGateway.McpSdk` only read from `IToolStore`.

## Rules for This Directory

- All public request/response types live in `Contracts/`.
- All domain exceptions live in `Exceptions/`.
- Services are `Scoped` and take repository interfaces + Core services via constructor injection.
- Keep DTOs as records; map to/from `McpServerDefinition` using static `FromDomain` / `ToDomain` helpers.
- Do not leak EF Core or HTTP status codes from services; throw the domain exceptions and let `AdminExceptionMiddleware` translate.
