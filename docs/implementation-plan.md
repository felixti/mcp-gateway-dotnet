# MCP Gateway (.NET 10) — Implementation Plan

## Phasing philosophy

Build the spine first (spec → tools → proxy → response), then layer on auth,
audit, management, and operational concerns. Each phase produces a runnable
gateway with incrementally more capability. Tests follow the shift-left
strategy: unit tests for each module, integration tests at each phase boundary,
BDD scenarios for business-level acceptance.

## Phase 1: Foundation + Spec Parsing (Week 1)

Goal: Parse an OpenAPI spec and generate ToolDefinitions in memory. No HTTP,
no database, no MCP SDK yet.

**Tasks:**
1. Create solution + 6 C# projects (Api, Core, Persistence, McpSdk, Management,
   Telemetry) with project references
2. Implement `OpenApiParser.cs` — parse OpenAPI 3.0+ JSON/YAML via
   Microsoft.OpenApi. Handle $ref inlining, cycle detection, circular ref
   dropping.
3. Implement `ToolNameResolver.cs` — operationId or method_path synthesis
4. Implement `DescriptionBuilder.cs` — prefer summary/description, synthesize
   fallback
5. Implement `PaginationDetector.cs` — detect limit/offset/cursor params,
   enhance description
6. Implement `SchemaTransformer.cs` — inline $refs, split anyOf (per
   ClientProfile), truncate names to 60 chars
7. Implement `ToolGenerator.cs` — orchestrate: parse → resolve names → build
   descriptions → transform schemas → produce List<ToolDefinition>
8. Unit tests: OpenApiParserTests, ToolGeneratorTests, SchemaTransformerTests,
   ToolNameResolverTests, PaginationDetectorTests

**Exit criteria:** Given a valid OpenAPI 3.0 spec, `ToolGenerator` produces a
list of ToolDefinitions with names, descriptions, input schemas, HTTP methods,
and paths. All unit tests green.

## Phase 2: MCP Endpoint + Tool Store (Week 2)

Goal: Serve tools/list from an in-memory store via the MCP Streamable HTTP
endpoint. No proxying yet — tools/call returns a stub.

**Tasks:**
1. Implement `IToolStore` + `InMemoryToolStore` (ConcurrentDictionary)
2. Implement `DynamicToolProvider` — custom IMcpServerTool that delegates to
   IToolStore (verify C# MCP SDK extension point — ADR-0003 risk)
3. Implement `McpEndpointMapper` — map `/mcp/{server_name}` routes with
   per-server tool sets
4. Implement `McpEndpoints.cs` — wire up MCP Streamable HTTP (stateless mode)
5. Implement `ToolStoreInitializer` — manually populate InMemoryToolStore for
   testing
6. Implement `HealthEndpoints.cs` — /health (liveness), /ready (stub for now)
7. Integration test: POST to `/mcp/test-server` with `tools/list` → returns
   tools from InMemoryToolStore
8. BDD test: "Generate tools from spec" feature

**Exit criteria:** MCP client can connect to `/mcp/{server_name}`, call
`tools/list`, and see generated tools. `tools/call` returns a stub response.

## Phase 3: Tool Call Proxy (Week 3)

Goal: Execute tool calls — construct HTTP request, proxy via HttpClient + Polly,
wrap response as MCP CallToolResult.

**Tasks:**
1. Implement `HttpRequestBuilder.cs` — path params → URL, query params →
   query string, body → request body
2. Implement `ResponseWrapper.cs` — HTTP response → MCP CallToolResult
   (truncated to 10KB, isError for non-2xx, HTTP status prefix)
3. Implement `ToolCallHandler.cs` — orchestrate: resolve tool → build request
   → send via HttpClient → wrap response → return CallToolResult
4. Configure IHttpClientFactory with named clients + Polly policies (retry 3x,
   circuit breaker 5 failures/30s, timeout 30s)
5. Implement `MetaToolsHandler.cs` — dynamic mode meta-tools
   (list_api_endpoints, get_api_endpoint_schema, invoke_api_endpoint)
6. Unit tests: HttpRequestBuilderTests, ResponseWrapperTests, ToolCallHandlerTests
7. Integration test: full tools/call flow with WireMock.Net as the underlying API
8. BDD test: "Proxy tool call" feature

**Exit criteria:** MCP client calls `tools/call` with args, gateway proxies to
the underlying API, returns the response as MCP content blocks. Polly retry and
circuit breaker work. Error responses wrapped with isError=true.

## Phase 4: Persistence + Management API (Week 4)

Goal: Persist server definitions and tools in PostgreSQL. Admin can register,
review, approve, and manage server definitions.

**Tasks:**
1. Implement `McpGatewayDbContext` — 5 entities, ai_gateway schema
2. Create EF Core initial migration (5 tables, indexes)
3. Implement repositories: ServerDefinitionRepository, ToolOverrideRepository,
   GatewayApiKeyRepository
4. Update `ToolStoreInitializer` — load approved server definitions from PG at
   startup
5. Implement `ServerManagementService` — register, list, get, update, delete,
   refresh, approve
6. Implement `ToolManagementService` — list tools, set override, toggle visibility
7. Implement `GatewayApiKeyService` — issue (bcrypt hash, return once), revoke,
   list
8. Implement `AdminEndpoints.cs` — wire up all admin API routes
9. Implement `SpecDiffService` — diff old vs new spec (added/removed/changed)
10. Implement `SpecRefresher` — manual refresh + polling background service
11. Integration tests with Testcontainers (PostgreSQL): full admin CRUD, spec
    refresh, approval workflow
12. BDD test: "Refresh spec detects new endpoints" feature

**Exit criteria:** Admin registers a spec via POST /admin/servers, tools are
generated and stored in PG, admin reviews and approves, tools appear in
tools/list. Spec refresh detects changes, sets changes_pending, admin
re-approves.

## Phase 5: Auth (Week 5)

Goal: Authenticate MCP clients (JWT + API key) and inject auth into proxied
calls (OBO + passthrough + static).

**Tasks:**
1. Implement `JwtAuthHandler.cs` — Entra ID JWT validation against JWKS
2. Implement `ApiKeyAuthHandler.cs` — look up gateway API key by hash, check
   scope
3. Implement `AuthResolver.cs` — determine which auth path per request
4. Implement `OboTokenExchange.cs` — Entra ID OBO flow (exchange caller JWT
   for scoped M2M token)
5. Implement `OboTokenCache.cs` — in-memory LRU with TTL, refresh before expiry
6. Implement `AuthStrategyResolver.cs` — resolve OBO/passthrough/static per
   server definition
7. Implement `AuthDelegatingHandler.cs` — DelegatingHandler on HttpClient
   pipeline, inject Authorization header per strategy
8. Wire auth into middleware pipeline (Program.cs)
9. Unit tests: OboTokenExchangeTests (mocked Entra ID), AuthStrategyResolverTests,
   ApiKeyAuthHandlerTests
10. Integration tests: OBO flow with mocked Entra ID token endpoint
    (WireMock.Net), API key auth, passthrough, static
11. BDD test: "Auth strategies" feature

**Exit criteria:** MCP client authenticates with JWT or API key. Tool calls to
OBO-strategy APIs carry exchanged M2M token. Per-user attribution in audit.
API key scoped to specific server definitions only.

## Phase 6: Audit + Telemetry (Week 6)

Goal: Every tool call is audited and traced. Operational visibility.

**Tasks:**
1. Implement `AuditEvent.cs` — schema: caller, auth method, auth strategy,
   tool name, args, response (truncated), HTTP status, latency, timestamp
2. Implement `QueueEmitter.cs` — fire-and-forget to Azure Storage Queue
3. Implement `DiskFallback.cs` — local disk buffer + retry worker
4. Implement `TelemetrySetup.cs` — OTLP/HTTP exporter, ActivitySources,
   metrics (call count, latency histogram, error rate, cache hit rate)
5. Implement `ActivitySources.cs` — McpGateway.ToolCall, McpGateway.OboExchange
6. Wire audit emission into ToolCallHandler (after response, before returning)
7. Wire telemetry into Program.cs (OpenTelemetry extensions)
8. Integration tests with Testcontainers (Azurite): audit event emission,
   disk fallback when queue is down, retry on recovery
9. Update /ready endpoint to check PG + Storage Queue connectivity
10. Implement `DependencyHealthChecker.cs` — background service for /ready checks

**Exit criteria:** Every tools/call produces an audit event in Azure Storage
Queue. Traces visible in Jaeger (local) / Dynatrace (stg/prd). /ready returns
503 when PG or Storage Queue is unreachable. Disk fallback works when queue is
down.

## Phase 7: Hardening + Deployment Prep (Week 7)

Goal: Production-ready. Graceful shutdown, configuration, Docker build, CI.

**Tasks:**
1. Implement graceful shutdown (IHostApplicationLifetime): drain connections,
   flush audit, flush disk fallback, close DbContext, exit
2. Configure Kestrel drain timeout (30s)
3. Configure appsettings.json with environment-based config
   (ConnectionStrings, EntraID, StorageQueue, Otlp)
4. Verify Dockerfile multi-stage build (dotnet sdk → aspnet runtime)
5. Set up pre-commit hook (dotnet format, build check)
6. Set up pre-push hook (unit tests)
7. Set up GitHub Actions CI: build, test (unit + integration + BDD), format,
   Docker build, security scan (Trivy)
8. Write seed-dev-data.sh script (populate dev PG with test specs)
9. Generate initial migration SQL script for DBA ticket
   (`dotnet ef migrations script --idempotent`)
10. k6 load test: tools/list and tools/call under load (100 RPS, 5min)
11. Security review: API key hashing, auth_config encryption at rest, JWT
    validation strictness, admin role check

**Exit criteria:** Gateway builds as Docker image. CI pipeline green. Graceful
shutdown handles SIGTERM within 35s. Load test passes at 100 RPS. Initial
migration SQL script ready for DBA ticket.

## Phase 8: Staging Deployment (Week 8)

Goal: Deploy to stg environment. End-to-end validation with real APIs.

**Tasks:**
1. Submit initial migration SQL script to DBA ticket for stg PG
2. Configure stg appsettings (Key Vault references for connection strings,
   Entra ID client/secret, Storage Queue connection, OTLP endpoint)
3. Deploy via company CI/CD templates to AKS stg cluster
4. Register a real internal API spec (e.g., invoice-api stg)
5. Admin approval flow end-to-end in stg
6. Connect Claude Code / Cursor to stg gateway
7. Validate: tools/list, tools/call, OBO auth, audit events, traces in
   Dynatrace
8. Verify graceful shutdown during K8s rollout (zero dropped requests)
9. Verify /ready behavior during dependency failures

**Exit criteria:** MCP clients connect to stg gateway, discover tools, invoke
them successfully against stg APIs. Audit events appear in stg Storage Queue.
Traces appear in Dynatrace. Graceful shutdown verified during pod rollout.

## Dependency graph

```
Phase 1 (Spec Parsing)
  └─► Phase 2 (MCP Endpoint + Tool Store)
        └─► Phase 3 (Tool Call Proxy)
              ├─► Phase 4 (Persistence + Management API)
              │     └─► Phase 5 (Auth)
              │           └─► Phase 6 (Audit + Telemetry)
              │                 └─► Phase 7 (Hardening + Deployment Prep)
              │                       └─► Phase 8 (Staging Deployment)
              └─► (Phase 4-6 can overlap if separate developers)
```

## Risk register

| Risk | Mitigation |
|------|-----------|
| C# MCP SDK doesn't expose clean mutable tool store extension point | Phase 2 tasks 2-3 are the spike. If SDK freezes tools at startup, implement custom IMcpServerTool that delegates to IToolStore. Verify before Phase 3. |
| EF Core migration SQL script incompatible with DBA process | Phase 4 task 2 generates the script early. Review with DBA team before Phase 7. |
| OBO token exchange latency on cache miss | In-memory LRU cache with TTL. Synchronous fallback ~50-100ms. Acceptable for financial company — correctness over latency. |
| Spec refresh disrupts active MCP sessions | changes_pending status pauses tool calls (not sessions). Clients get -32005 and can retry after admin approves. No session disconnect. |
| Prompt injection via tool descriptions | Admin approval workflow (Q20 resolution). Admin reviews all descriptions before activation. Spec refresh triggers re-approval. |