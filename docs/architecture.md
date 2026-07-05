# MCP Gateway (.NET 10) — Architecture

## Overview

The MCP Gateway is a control plane that turns backends into MCP (Model Context
Protocol) servers. It supports two **source types**: `openapi` (generates tools
from an OpenAPI 3.0+ specification and proxies calls as HTTP) and `mcp-upstream`
(imports an existing MCP server's tool catalog and forwards calls as JSON-RPC).
MCP clients (Claude Desktop, Cursor, Claude Code, Codex CLI, custom apps)
connect to the gateway, discover tools, and invoke them. The gateway handles
auth injection (OBO/passthrough/static), approval, audit, and telemetry.

For `openapi` servers the underlying APIs are unchanged — no MCP SDK required on
the API side. For `mcp-upstream` servers the gateway acts as an MCP client to
the upstream server, re-hosting its catalog behind one approval/audit/auth plane.

## System diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        MCP Clients                                │
│  Claude Desktop │ Cursor │ Claude Code │ Codex CLI │ Custom Apps │
└──────────┬──────────────────┬───────────────────────────────────┘
           │                  │
     API key (mgk_)     Entra ID JWT
           │                  │
           ▼                  ▼
┌──────────────────────────────────────────────────────────────────┐
│                     MCP Gateway (.NET 10)                         │
│                                                                   │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────────────────┐ │
│  │ MCP Endpoint│  │ Admin API    │  │ Health Endpoints         │ │
│  │ /mcp/{name} │  │ /admin/*     │  │ /health, /ready          │ │
│  └──────┬──────┘  └──────┬───────┘  └──────────────────────────┘ │
│         │                │                                        │
│         ▼                ▼                                        │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                     Core Pipeline                            │ │
│  │                                                               │ │
│  │  Auth ──► Resolve Server ──► Resolve Tool ──► Strategy         │ │
│  │    │                           dispatch by SourceType          │ │
│  │    │                                │                          │ │
│  │    │                    ┌───────────┴───────────┐              │ │
│  │    │                    ▼                       ▼              │ │
│  │    │     HttpInvocationStrategy     McpUpstreamInvocation      │ │
│  │    │       Build HTTP + Proxy        Strategy                  │ │
│  │    │       HttpClient + Polly         SdkMcpUpstreamClient     │ │
│  │    │       + Auth Handler            JSON-RPC tools/call       │ │
│  │    │                                                         │ │
│  │    ├──► OBO Token Exchange ──► Entra ID                      │ │
│  │    ├──► Audit Emitter ────► Azure Storage Queue              │ │
│  │    └──► Telemetry ─────► OTLP ──► Dynatrace                  │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                    In-Memory State                           │ │
│  │  InMemoryToolStore (ConcurrentDictionary)                    │ │
│  │    └── loaded from PG at startup, only approved servers      │ │
│  │  OboTokenCache (LRU, TTL-based)                              │ │
│  └─────────────────────────────────────────────────────────────┘ │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                  Background Services                         │ │
│  │  SpecRefresher (polling, 24h default)                        │ │
│  │  ToolStoreInitializer (startup: PG → InMemory)               │ │
│  │  DependencyHealthChecker (PG + Storage Queue for /ready)     │ │
│  │  DiskFallbackRetryWorker (audit retry if queue was down)     │ │
│  └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────┬────────────────────────────────────────┘
                           │
     ┌──────────────┐ ┌─────────┐ ┌──────────────────┐
     │ PostgreSQL   │ │ Azure   │ │  Underlying APIs  │
     │ (ai_gateway) │ │ Storage │ │  (OpenAPI / MCP)  │
     │              │ │ Queue   │ │                   │
     │ 5 tables:    │ └─────────┘ └──────────────────┘
     │ - mcp_       │
     │   server_    │     ┌─────────┐
     │   defs       │────►│ Azure   │
     │ - tools      │     │ Blob    │ (audit long-term)
     │ - tool_      │     │ Storage │
     │   overrides  │     └─────────┘
     │ - gateway_   │
     │   api_keys   │     ┌─────────┐
     │ - spec_      │────►│Dynatrace│ (OTLP traces/metrics)
     │   versions   │     └─────────┘
     └──────────────┘
```

## Component responsibilities

### McpGateway.Api (ASP.NET Core 10 web app)

Entry point. Hosts the Kestrel server, middleware pipeline, DI container, and
all HTTP endpoints. Three endpoint groups:

- **MCP endpoints** (`/mcp/{server_name}`) — MCP Streamable HTTP. Each route
  serves tools from one server definition. Stateless mode (no server-to-client
  requests). Auth: Entra ID JWT or gateway API key.
- **Admin API** (`/admin/*`) — management CRUD for server definitions, tools,
  API keys, spec management. Auth: Entra ID JWT with admin role claim.
- **Health endpoints** (`/health`, `/ready`) — liveness and readiness probes
  for Kubernetes.

### McpGateway.Core (domain logic, no HTTP deps)

The pipeline spine. Contains:

- **ToolGeneration** — OpenApiParser (Microsoft.OpenApi), ToolGenerator,
  SchemaTransformer (inline $refs, split anyOf, truncate names per ClientProfile),
  ToolNameResolver, DescriptionBuilder, PaginationDetector.
- **ToolStore** — IToolStore interface + InMemoryToolStore
  (ConcurrentDictionary). The runtime truth for tools/list and tools/call.
  ToolStoreInitializer loads approved server definitions from PG at startup.
- **Proxy** — ToolCallHandler dispatches by `SourceType` to the matching
  IToolInvocationStrategy. For `openapi`: HttpInvocationStrategy builds an HTTP
  request (HttpRequestBuilder), proxies via HttpClient + Polly + AuthDelegatingHandler,
  wraps the response (ResponseWrapper — truncation, isError for non-2xx). For
  `mcp-upstream`: McpUpstreamInvocationStrategy forwards the call via
  SdkMcpUpstreamClient (real MCP SDK client over Streamable HTTP).
  MetaToolsHandler provides dynamic-mode meta-tools. ToolCallResult carries
  HttpStatus for audit/telemetry.
- **McpUpstream** — UpstreamCatalogImporter (pure mapper: upstream `tools/list`
  → ToolDefinition with null HTTP coords), IMcpUpstreamClient / SdkMcpUpstreamClient
  (MCP SDK client: ListToolsAsync + CallToolAsync over Streamable HTTP transport),
  UpstreamTool (DTO: name, description, JsonNode? input schema).
- **Auth** — OboTokenExchange (Entra ID OBO flow), OboTokenCache (LRU + TTL),
  AuthStrategyResolver (OBO/passthrough/static per server definition),
  AuthDelegatingHandler (injects auth header into HttpClient pipeline).
- **Audit** — AuditEvent schema, QueueEmitter (fire-and-forget to Azure Storage
  Queue), DiskFallback (local buffer + retry worker if queue is down).
- **SpecManagement** — SpecDiffService (diff old vs new spec), SpecFetcher
  (fetch from URL or file), SpecRefresher (manual + polling background service).

### McpGateway.Persistence (EF Core + PostgreSQL)

DbContext with `HasDefaultSchema("ai_gateway")`. 5 tables (mcp_server_defs,
tools, tool_overrides, gateway_api_keys, spec_versions). Repository pattern
over EF Core. Migrations are EF Core migration classes applied via
`Database.MigrateAsync()` (the InitialCreate migration + additive migrations
like AddSourceTypeAndNullableToolCoords). `SourceType` is stored as a canonical
string (`openapi` / `mcp-upstream`) via a ValueConverter; tool `HttpMethod` /
`HttpPath` are nullable to support `mcp-upstream` tools.

### McpGateway.McpSdk (wrapper over C# MCP SDK)

McpSdkServiceExtensions configures the MCP server with custom `tools/list`
and `tools/call` handlers that delegate to IToolStore and ToolCallHandler
respectively. `MapMcpGateway` maps `/mcp/{server_name}` routes. The call
handler resolves the server from the store, checks approval, then dispatches
to ToolCallHandler which selects the invocation strategy by SourceType.
Stateless Streamable HTTP mode is used (no session ID required).

### McpGateway.Management (management API logic)

Services used by AdminEndpoints: ServerManagementService (register, list, get,
update, delete, refresh, approve), ToolManagementService (list, override,
visibility), GatewayApiKeyService (issue, revoke), ClientProfileService.

### McpGateway.Telemetry (OpenTelemetry)

OTLP/HTTP exporter to Dynatrace. ActivitySources:
`McpGateway.ToolCall`, `McpGateway.OboExchange`. Metrics: tool call count,
latency histogram, error rate, OBO cache hit rate.

## Request flow: tools/call

```
MCP Client
  │
  │  POST /mcp/{server_name}
  │  Body: {"method":"tools/call","params":{"name":"get_invoices","arguments":{...}}}
  │  Auth: Bearer <Entra ID JWT> or X-Gateway-Key: mgk_...
  │
  ▼
JwtAuthHandler / ApiKeyAuthHandler
  │  Validate auth, extract caller identity
  │
  ▼
McpEndpointMapper
  │  Route to /mcp/{server_name}
  │  Load McpServerDefinition from InMemoryToolStore
  │  Check approval_status — reject with -32005 if not approved
  │
  ▼
McpSdkServiceExtensions (tools/call handler)
  │  Look up tool by name in IToolStore
  │  Reject if not found or not visible
  │
  ▼
ToolCallHandler
  │  Select IToolInvocationStrategy by server.SourceType:
  │
  │  ┌─ openapi → HttpInvocationStrategy ──────────────────────┐
  │  │  1. Build HTTP request (HttpRequestBuilder)              │
  │  │  2. Resolve auth strategy (AuthStrategyResolver)         │
  │  │     - OBO: OboTokenExchange → Entra ID → scoped token    │
  │  │     - passthrough: forward caller JWT directly           │
  │  │     - static: use stored API key                         │
  │  │  3. AuthDelegatingHandler injects Authorization header   │
  │  │  4. HttpClient + Polly: send request (retry, CB, timeout)│
  │  │  5. ResponseWrapper: HTTP response → CallToolResult      │
  │  │     - 2xx: content blocks with body (truncated to 10KB)  │
  │  │     - non-2xx: isError=true, "[HTTP {status}] {body}"   │
  │  └──────────────────────────────────────────────────────────┘
  │
  │  ┌─ mcp-upstream → McpUpstreamInvocationStrategy ─────────┐
  │  │  SdkMcpUpstreamClient connects to upstream via         │
  │  │  Streamable HTTP, forwards tools/call as JSON-RPC,     │
  │  │  maps result content blocks → CallToolResult           │
  │  └──────────────────────────────────────────────────────────┘
  │
  ▼
AuditEmitter (fire-and-forget)
  │  Emit AuditEvent to Azure Storage Queue
  │  Disk fallback if queue is unavailable
  │
  ▼
MCP Client
  │  Receives CallToolResult (JSON-RPC 2.0 response)
```

## Request flow: admin approval (new server definition)

```
Admin
  │
  │  POST /admin/servers
  │  Body: {name, source_type, spec_source_url|upstream_url, base_url, ...}
  │  Auth: Bearer <Entra ID JWT> with admin role
  │
  ▼
ServerManagementService.RegisterAsync
  │  Branch on source_type:
  │
  │  ┌─ openapi (default) ─────────────────────────────────────┐
  │  │  1. Fetch + parse spec (SpecFetcher → OpenApiParser)     │
  │  │  2. Generate tools (ToolGenerator + SchemaTransformer)   │
  │  └──────────────────────────────────────────────────────────┘
  │
  │  ┌─ mcp-upstream ──────────────────────────────────────────┐
  │  │  1. Connect to upstream via SdkMcpUpstreamClient         │
  │  │  2. Call upstream tools/list                              │
  │  │  3. Import catalog via UpstreamCatalogImporter            │
  │  │     (tools get null HttpMethod/HttpPath)                  │
  │  └──────────────────────────────────────────────────────────┘
  │
  │  Store in PG: mcp_server_defs (source_type, approval_status='pending'), tools
  │
  ▼
Admin reviews tools
  │  GET /admin/servers/{name}/tools
  │  Inspect descriptions for prompt injection, verify tool names, check schemas
  │
  ▼
Admin approves
  │  POST /admin/servers/{name}/approve
  │
  ▼
ServerManagementService
  │  1. Set approval_status='approved', approved_at=now(), approved_by=admin UPN
  │  2. Load tools into InMemoryToolStore (hot-reload, no restart)
  │  3. Next tools/list from any MCP client returns the new tools
```

## Request flow: spec refresh (polling or manual)

```
SpecRefresher (background service, 24h default)
  │
  │  1. Fetch spec from spec_source_url
  │  2. Compute SHA256 hash
  │  3. Compare with stored spec_hash
  │  4. If unchanged → done
  │  5. If changed:
  │     a. Parse new spec
  │     b. Diff old vs new (SpecDiffService): added/removed/changed tools
  │     c. Store new spec snapshot in spec_versions
  │     d. Update tools in PG
  │     e. Set approval_status='changes_pending'
  │     f. Remove old tools from InMemoryToolStore
  │     g. MCP clients get -32005 on tools/call until admin re-approves
  │
  ▼
Admin reviews changes
  │  GET /admin/servers/{name}/tools/diff
  │  See what changed (added, removed, description changes)
  │
  ▼
Admin re-approves
  │  POST /admin/servers/{name}/approve
  │  → New tools loaded into InMemoryToolStore
```

## Deployment topology

One gateway deployment per environment: **dev**, **stg**, **prd**. No
cross-environment routing. Each environment has:

- Its own gateway instance (AKS pod, managed by company CI/CD templates)
- Its own PostgreSQL database (ai_gateway schema)
- Its own Azure Storage Queue + Blob Storage (audit pipeline)
- Its own OTLP endpoint (Dynatrace)

The gateway codebase is identical across environments. Configuration
(connection strings, Entra ID tenant/client, OTLP endpoint, storage account)
is injected via environment variables / Azure Key Vault references in
appsettings.json.

```
┌─────────── dev environment ───────────┐
│  MCP Gateway ──► dev PG (ai_gateway)  │
│             ──► dev Storage Queue     │
│             ──► dev Dynatrace tenant  │
│             ──► dev APIs (base_urls)  │
└────────────────────────────────────────┘

┌─────────── stg environment ───────────┐
│  MCP Gateway ──► stg PG (ai_gateway)  │
│             ──► stg Storage Queue     │
│             ──► stg Dynatrace tenant  │
│             ──► stg APIs (base_urls)  │
└────────────────────────────────────────┘

┌─────────── prd environment ───────────┐
│  MCP Gateway ──► prd PG (ai_gateway)  │
│             ──► prd Storage Queue     │
│             ──► prd Dynatrace tenant  │
│             ──► prd APIs (base_urls)  │
└────────────────────────────────────────┘
```

## Graceful shutdown and degradation

The gateway is designed for Kubernetes lifecycle management. The company CI/CD
templates handle deployment manifests, scaling, and rollout. The gateway
handles:

### Graceful shutdown (SIGTERM)

On SIGTERM (K8s rollout/scale-down):

1. Stop accepting new connections (Kestrel drain)
2. Set `/ready` to return 503 immediately (K8s removes from Service)
3. Wait for in-flight tool calls to complete (timeout: 30s)
4. Flush audit events from memory to Azure Storage Queue
5. Flush disk fallback buffer to queue
6. Close OBO token cache (in-memory, no cleanup needed)
7. Close EF Core DbContext / connection pool
8. Exit cleanly

K8s `terminationGracePeriodSeconds`: 35s (30s drain + 5s cleanup).

If SIGKILL arrives (timeout exceeded): in-flight tool calls are lost.
Telemetry span shows incomplete call — detectable in monitoring.

### Degradation modes

| Failure | Behavior |
|---------|----------|
| PostgreSQL down | Gateway cannot start. If PG goes down at runtime, /ready returns 503. In-flight tool calls complete using InMemoryToolStore. New tool calls fail at audit emission (disk fallback). Admin API returns 503. |
| Azure Storage Queue down | Audit events buffer to local disk. DiskFallbackRetryWorker retries when queue recovers. Tool calls continue normally — audit is fire-and-forget. |
| Entra ID down (OBO) | OBO token exchange fails. Tool calls to OBO-strategy APIs return error (isError=true). Passthrough and static strategies unaffected. Cached tokens used until expiry. |
| Underlying API down | Polly circuit breaker opens after 5 failures → tool calls return error immediately (isError=true, "[HTTP 503] circuit breaker open"). Circuit resets after 30s. |
| Dynatrace down | Telemetry export fails silently. No impact on tool calls. OTLP exporter retries with backoff. |

## Data flow summary

```
                    ┌──────────────────────────┐
                    │     PostgreSQL            │
                    │     (ai_gateway schema)   │
                    │                           │
  Admin API ───────►│  mcp_server_defs          │
  (write)           │  tools                    │
                    │  tool_overrides           │
                    │  gateway_api_keys         │
                    │  spec_versions            │
                    └────────┬─────────────────┘
                             │ load at startup
                             │ + hot-reload on approval
                             ▼
                    ┌──────────────────────────┐
                    │  InMemoryToolStore        │
                    │  (ConcurrentDictionary)   │
                    │                           │
  MCP Client ──────►│  tools/list reads here    │──► ToolCallHandler
  (read + call)     │  tools/call reads here    │    │ strategy dispatch
                    └──────────────────────────┘    │ by SourceType
                                                     │
                              ┌──────────────────────┴──────────────────────┐
                              ▼                                              ▼
                    HttpInvocationStrategy                       McpUpstreamInvocationStrategy
                    (HttpClient + Polly + Auth)                   (SdkMcpUpstreamClient → JSON-RPC)
                              │                                              │
                              ▼                                              ▼
                    Underlying API (HTTP)                           Upstream MCP Server
                                                      │
                                                      ▼
                                               Audit Emitter
                                                      │
                                                      ▼
                                               Azure Storage Queue
                                                      │
                                                      ▼
                                               Azure Blob Storage
```

## Security model

### Gateway auth (MCP client → gateway)

Two paths:
1. **Entra ID JWT** — validated against JWKS. For Claude Code, Codex CLI,
   custom apps. Caller identity extracted from token claims.
2. **Gateway API key** (`mgk_...`) — bcrypt-hashed in PG, scoped to specific
   server definitions. For Claude Desktop, Cursor (no bearer token support).

### API auth (gateway → underlying API)

Three strategies, configured per server definition:
1. **OBO (default)** — exchange caller's JWT for scoped M2M token via Entra ID
   OBO flow. Per-user attribution preserved end-to-end.
2. **Passthrough** — forward caller's JWT directly. Same Entra ID app
   registration required.
3. **Static** — stored API key/bearer. No per-user attribution.

### Admin approval (prompt injection defense)

All new server definitions and spec-refresh changes require admin approval
before tools are served. `approval_status` column on `mcp_server_defs`:
`pending` → `approved` (admin reviews tool descriptions) or `changes_pending`
(spec changed since last approval). Hot-reload only loads `approved` servers.
Unapproved servers return JSON-RPC error -32005 on tools/call.

## Technology stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10 (ASP.NET Core 10) |
| Language | C# 13 |
| MCP SDK | ModelContextProtocol 2.0.0-preview.1 (server: AspNetCore; client: upstream MCP calls) |
| OpenAPI parser | Microsoft.OpenApi |
| ORM | EF Core 10 |
| Database | PostgreSQL 18 |
| Resilience | Polly (via IHttpClientFactory) |
| Auth | Microsoft.Identity.Web (Entra ID JWT + OBO) |
| Audit | Azure Storage Queue + Blob Storage |
| Telemetry | OpenTelemetry + OTLP/HTTP → Dynatrace |
| Testing | xUnit + Testcontainers + Reqnroll (BDD) |
| Deployment | AKS (company CI/CD templates) |
| Local dev | docker-compose (gateway + PG + Azurite + Jaeger) |