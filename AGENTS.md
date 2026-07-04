# MCP Gateway — Agent Context

## Project Identity

A .NET 10 ASP.NET Core gateway that turns any OpenAPI 3.0+ web API into an
MCP (Model Context Protocol) server. One OpenAPI spec = one **MCP Server
Definition**. The gateway exposes MCP tools, proxies `tools/call` to the
underlying HTTP API, handles auth (OBO / passthrough / static), emits audit
events and OpenTelemetry telemetry, and provides a minimal-API management
surface (`/admin/servers/*`) for registration and approval.

Environment split is deployment-level: one gateway instance per environment
(dev, stg, prd), each with its own database and set of server definitions.
No cross-environment routing inside a single gateway.

## Repository Layout

```
/
├── src/
│   ├── McpGateway.Api/            # Composition root, Program.cs, auth handlers, MCP endpoint wiring
│   ├── McpGateway.Core/           # Domain services: tool generation, spec refresh, proxy, auth orchestration
│   ├── McpGateway.Management/     # Admin API services, auth, exceptions, contracts
│   ├── McpGateway.Persistence/    # EF Core, repositories, migrations
│   ├── McpGateway.McpSdk/         # Thin adapter over ModelContextProtocol.AspNetCore
│   └── McpGateway.Telemetry/      # OpenTelemetry + Dynatrace setup
├── tests/
│   ├── McpGateway.UnitTests/      # Fast isolated unit tests (130)
│   ├── McpGateway.IntegrationTests/ # HTTP-in-memory integration tests (48)
│   └── McpGateway.BddTests/       # Reqnroll BDD scenarios (20)
├── .husky/                        # Husky.Net git hooks
├── .github/workflows/ci.yml       # CI pipeline
└── McpGateway.sln
```

## Technology Stack

- **Runtime / Framework**: .NET 10, ASP.NET Core, EF Core 10 (preview)
- **MCP**: `ModelContextProtocol.AspNetCore` 2.0.0-preview.1 (Streamable HTTP endpoint)
- **OpenAPI**: `Microsoft.OpenApi` 1.6.23 for reading 3.0 specs
- **Auth**: Microsoft.Identity.Web for Entra ID / OBO; API-key middleware for
  legacy MCP clients
- **Resilience**: Polly (HttpClient policies)
- **Telemetry**: OpenTelemetry.Extensions.AspNetCore + OTLP/HTTP to Dynatrace
- **Testing**: xUnit, Reqnroll, Testcontainers.PostgreSQL (integration), HTTP
  in-memory `WebApplicationFactory`
- **Shift-left**: Husky.Net, `dotnet format`, `.editorconfig`, GitHub Actions

## Build & Test Commands

```bash
# Restore and build
dotnet build McpGateway.sln

# Run all tests
dotnet test McpGateway.sln --no-build

# Format verification (also runs in pre-commit)
dotnet format McpGateway.sln --verify-no-changes --verbosity diagnostic

# Apply formatting
dotnet format McpGateway.sln

# Run hooks manually
dotnet husky run --group pre-commit
dotnet husky run --group pre-push
```

## Universal Conventions

- Language: C# 13 / .NET 10 idioms.
- Top-level statements are used only in `Program.cs`; everything else uses
  explicit namespaces and classes.
- Records are used for immutable data/contracts; classes for behavior/services.
- `ConfigureAwait(false)` is omitted in ASP.NET Core / test code (synchronisation
  context is not used).
- Async methods are suffixed with `Async`.
- `CancellationToken` is always forwarded from callers.
- Do not suppress warnings with `#pragma`, `as any`, or null-forgiving operators
  without a recorded reason in the code review.
- Prefer constructor injection over service location.
- Prefer `IReadOnly*` collections in public APIs.
- Prefer `TimeProvider` and `HttpMessageHandler` abstractions for testability.
- Keep domain exceptions in `*.Exceptions` namespaces near the feature that
  throws them.

## Key Domain Terms

Use the terminology from `CONTEXT.md`:

- **Auth Strategy**: how the gateway authenticates to the upstream API
  (`OBO`, `passthrough`, `static`).
- **Gateway Auth**: how MCP clients authenticate to the gateway
  (`EntraIdJwt`, `ApiKey`).
- **OBO Handler**: performs Entra ID On-Behalf-Of token exchange.
- **MCP Server Definition**: a persisted OpenAPI spec + derived tools + config.
- **Tool**: a single MCP tool generated from one `(path, method)` pair.
- **Tool Mode**: `all` | `dynamic` | `curated`.
- **Client Profile**: `universal` | `claude` | `cursor`.
- **Audit Trail**: fire-and-forget log of every MCP tool call.

## Entry Points & Boot Flow

1. `src/McpGateway.Api/Program.cs` builds the web host, wires middleware,
   registers `AddMcpCore()`, `AddMcpPersistence(...)`, `AddMcpTelemetry()`,
   `AddMcpManagement()` and `AddMcpGatewayMcp()`.
2. `SpecRefresher` is a hosted service that loads approved server definitions
   from PostgreSQL into the in-memory `IToolStore` at startup.
3. HTTP requests to `/mcp/{serverName}` are routed through the MCP SSE
   middleware; `tools/call` is handled by `McpToolCallHandler` →
   `ToolCallHandler.HandleAsync`.
4. Management API lives under `/admin/servers/*` and uses minimal APIs + admin auth.

## External Dependencies & Warnings

- `McpGateway.BddTests` currently shows NU1510 / NU1903 package warnings for
  `Microsoft.NET.Test.Sdk`. These are pre-existing and do not block tests.
- EF Core is a preview package; migrations may need regenerating after SDK
  updates.

## Approval Gate

New or refreshed server definitions require admin approval before tools are
served. Unapproved definitions return JSON-RPC error `-32005`.
