# `McpGateway.Api` — Agent Context

## Scope

The ASP.NET Core **composition root** and **HTTP surface**. It composes all sibling projects, registers authentication/authorization, maps endpoints, wires middleware, and owns the only `Program.cs`.

## Boot Order in `Program.cs`

1. `AddMemoryCache`
2. `AddMcpPersistence(...)`
3. `AddMcpTelemetry()`
4. `AddMcpAudit()`
5. `AddMcpProxy()`
6. `AddMcpCore()`
7. `AddMcpHealth()`
8. `AddMcpManagement()`
9. `AddMcpGatewayMcp()`
10. `AddSingleton(TimeProvider.System)`

Pipeline order:
```
UseAdminExceptionHandler → UseAuthentication → UseAuthorization
  → MapHealthEndpoints → MapMcp → MapAdminEndpoints
```

## Auth

| Scheme | Handler | Purpose |
|---|---|---|
| `EntraIdJwt` | `Auth/JwtAuthHandler.cs` | MCP client via Entra ID bearer token |
| `GatewayApiKey` | `Auth/ApiKeyAuthHandler.cs` | MCP client via `X-Gateway-Key` header (BCrypt hash) |
| `Admin` | `Auth/AdminAuthHandler.cs` | Admin via Entra ID with `mcp-gateway-admin` role |
| `DevAdmin` | `Auth/DevelopmentAdminAuthHandler.cs` | Dev-only `X-Dev-Admin` bypass |

Policies:
- `McpClient` — `EntraIdJwt` OR `GatewayApiKey`
- `Admin` — `Admin` OR `DevAdmin`

## Endpoints

| File | Route base | Notes |
|---|---|---|
| `Endpoints/HealthEndpoints.cs` | `/health`, `/ready` | Anonymous liveness + readiness probe aggregation |
| `Endpoints/McpEndpoints.cs` | `/mcp/{serverName}` | One-liner: `MapMcpGateway().RequireAuthorization("McpClient")` |
| `Endpoints/AdminEndpoints.cs` | `/admin/servers/*` | CRUD, refresh, approve, tools, overrides, API keys, spec upload/diff; includes `AdminExceptionMiddleware` |

## Key Configuration Sections

- `EntraId:TenantId`, `EntraId:Audience`, `EntraId:JwksUri` — JWT validation.
- `AdminAuth:Role` — admin role name.
- `DevelopmentAdmin:HeaderName` — dev bypass header.
- `Kestrel` limits — max request body 10 MB.
- `HostOptions:ShutdownTimeout` — 35 seconds.
- Services start/stop are **non-concurrent** for ordered graceful shutdown.

## Rules for This Directory

- `Program.cs` is the only top-level-statements file in the repo.
- Keep auth handlers in `Auth/` and endpoints in `Endpoints/`.
- `AdminExceptionMiddleware` currently lives in `AdminEndpoints.cs`; promote it to its own file only when a second endpoint group needs the same handling.
- Do not add business logic here — delegate to `McpGateway.Management` or `McpGateway.Core`.
