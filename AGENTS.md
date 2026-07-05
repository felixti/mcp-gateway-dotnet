# MCP Gateway — Agent Context

## Project Identity

A .NET 10 ASP.NET Core gateway that turns backends into MCP (Model Context
Protocol) servers. It supports two **source types**: `openapi` (generates
tools from an OpenAPI 3.0+ spec and proxies HTTP calls) and `mcp-upstream`
(imports an existing MCP server's tool catalog and forwards JSON-RPC calls).
One spec or upstream = one **MCP Server Definition**. The gateway exposes MCP
tools, handles auth (OBO / passthrough / static), emits audit events and
OpenTelemetry telemetry, and provides a minimal-API management surface
(`/admin/servers/*`) for registration and approval.

Environment split is deployment-level: one gateway instance per environment
(dev, stg, prd), each with its own database and set of server definitions.
No cross-environment routing inside a single gateway.

## Repository Layout

```
/
│   ├── McpGateway.Core/           # Domain: tool generation, spec refresh, proxy (strategy dispatch), McpUpstream client/importer, auth
│   ├── McpGateway.Management/     # Admin API services, auth, exceptions, contracts (SourceType branch in RegisterAsync)
│   ├── McpGateway.Persistence/    # EF Core, repositories, migrations (incl. SourceType + nullable HTTP coords)
│   ├── McpGateway.McpSdk/         # Thin adapter over ModelContextProtocol.AspNetCore (custom tools/list + tools/call handlers)
│   └── McpGateway.Telemetry/      # OpenTelemetry + Dynatrace setup
├── tests/
│   ├── McpGateway.UnitTests/      # Fast isolated unit tests (158)
│   ├── McpGateway.IntegrationTests/ # HTTP-in-memory + Testcontainers integration tests (56)
│   └── McpGateway.BddTests/       # Reqnroll BDD scenarios (20)
├── .husky/                        # Husky.Net git hooks
├── .github/workflows/ci.yml       # CI pipeline
└── McpGateway.sln
```

## Technology Stack

- **Runtime / Framework**: .NET 10, ASP.NET Core, EF Core 10 (preview)
- **MCP**: `ModelContextProtocol` 2.0.0-preview.1 — server-side via `ModelContextProtocol.AspNetCore` (Streamable HTTP endpoint); client-side via `ModelContextProtocol.Client` (upstream MCP calls for `mcp-upstream` source type)
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
- **MCP Server Definition**: a persisted spec or upstream catalog + derived tools + config.
- **Source Type**: `openapi` (tools from spec, proxied as HTTP) | `mcp-upstream` (tools from upstream `tools/list`, forwarded as JSON-RPC).
- **Tool**: a single MCP tool. For `openapi`: generated from a `(path, method)` pair (has HttpMethod/HttpPath). For `mcp-upstream`: imported from upstream catalog (null HttpMethod/HttpPath).
- **Tool Mode**: `all` | `dynamic` | `curated`.
- **Client Profile**: `universal` | `claude` | `cursor`.
- **Audit Trail**: fire-and-forget log of every MCP tool call.
- **IToolInvocationStrategy**: dispatch interface — `HttpInvocationStrategy` (openapi) or `McpUpstreamInvocationStrategy` (mcp-upstream). `ToolCallHandler` selects by `SourceType`.

## Entry Points & Boot Flow

1. `src/McpGateway.Api/Program.cs` builds the web host, wires middleware,
   registers `AddMcpCore()`, `AddMcpPersistence(...)`, `AddMcpTelemetry()`,
   `AddMcpManagement()` and `AddMcpGatewayMcp()`.
2. `SpecRefresher` is a hosted service that loads approved server definitions
   from PostgreSQL into the in-memory `IToolStore` at startup.
3. HTTP requests to `/mcp/{serverName}` are routed through the MCP SSE
   middleware; `tools/call` is handled by `McpSdkServiceExtensions` →
   `ToolCallHandler.HandleAsync`, which dispatches to the matching
   `IToolInvocationStrategy` by `SourceType` (`HttpInvocationStrategy` for
   openapi, `McpUpstreamInvocationStrategy` for mcp-upstream).
4. Management API lives under `/admin/servers/*` and uses minimal APIs + admin
   auth. `RegisterAsync` branches on `SourceType`: openapi fetches/parses spec
   and generates tools; mcp-upstream calls the upstream's `tools/list` via
   `SdkMcpUpstreamClient` and imports the catalog via `UpstreamCatalogImporter`.

## External Dependencies & Warnings

- `McpGateway.BddTests` currently shows NU1510 / NU1903 package warnings for
  `Microsoft.NET.Test.Sdk`. These are pre-existing and do not block tests.
- EF Core is a preview package; migrations may need regenerating after SDK
  updates.

## Approval Gate

New or refreshed server definitions require admin approval before tools are
served. Unapproved definitions return JSON-RPC error `-32005`.
