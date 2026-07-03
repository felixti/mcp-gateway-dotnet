# MCP Gateway (.NET 10) — Project Structure

## Design principles

1. **Pipeline-first** — the tool-call flow is the spine: auth → resolve server → resolve tool → construct HTTP → proxy → wrap response → audit. A developer reads `ToolCallHandler.cs` top-to-bottom and understands the full flow.
2. **Spec isolation** — OpenAPI parsing, tool generation, and schema transformation live in one module. Adding a new OpenAPI version or schema transform = one directory.
3. **Per-API isolation** — each registered API gets its own HttpClient via IHttpClientFactory with named clients. Auth injection is a DelegatingHandler per client.
4. **Shared kernel** — config, auth, telemetry, persistence are infrastructure used by everything.
5. **AI-friendly** — one convention, no surprises. A file's purpose is obvious from its path.

## Directory structure

```
McpGateway/
├── src/
│   ├── McpGateway.Api/                    # ASP.NET Core 10 web app (entry point)
│   │   ├── Program.cs                     # App startup, DI registration, middleware pipeline
│   │   ├── McpGateway.Api.csproj
│   │   ├── Endpoints/
│   │   │   ├── McpEndpoints.cs             # /mcp/{server_name} — MCP Streamable HTTP endpoint
│   │   │   ├── AdminEndpoints.cs            # /admin/* — management CRUD
│   │   │   └── HealthEndpoints.cs           # /health, /ready
│   │   ├── Auth/
│   │   │   ├── JwtAuthHandler.cs            # Entra ID JWT validation middleware
│   │   │   ├── ApiKeyAuthHandler.cs         # Gateway API key validation middleware
│   │   │   └── AuthResolver.cs             # Determines which auth path (JWT or API key) per request
│   │   └── appsettings.json
│   │
│   ├── McpGateway.Core/                   # Core domain logic (no HTTP, no ASP.NET deps)
│   │   ├── McpGateway.Core.csproj
│   │   ├── ServerDefinitions/
│   │   │   ├── McpServerDefinition.cs       # Domain model: server definition + tools
│   │   │   ├── ToolDefinition.cs           # Domain model: single tool (name, schema, handler ref)
│   │   │   ├── ToolMode.cs                  # Enum: All, Dynamic, Curated
│   │   │   └── ClientProfile.cs            # Enum: Universal, Claude, Cursor
│   │   ├── ToolGeneration/
│   │   │   ├── OpenApiParser.cs            # Parse OpenAPI spec via Microsoft.OpenApi
│   │   │   ├── ToolGenerator.cs             # Generate ToolDefinitions from parsed spec
│   │   │   ├── SchemaTransformer.cs        # Inline $refs, split anyOf, truncate names (per ClientProfile)
│   │   │   ├── ToolNameResolver.cs         # operationId or method_path synthesis
│   │   │   ├── DescriptionBuilder.cs       # Prefer summary/description, synthesize fallback
│   │   │   └── PaginationDetector.cs       # Detect pagination params, enhance description with pagination contract
│   │   ├── ToolStore/
│   │   │   ├── IToolStore.cs               # Interface: GetServer(name), GetAllServers(), AddServer(), UpdateServer(), RemoveServer()
│   │   │   ├── InMemoryToolStore.cs        # ConcurrentDictionary<serverName, McpServerDefinition> — the hot-reload cache
│   │   │   └── ToolStoreInitializer.cs     # On startup: load all definitions from PG into InMemoryToolStore
│   │   ├── Proxy/
│   │   │   ├── ToolCallHandler.cs          # The core: (tool name, args, server context) → HTTP request → response → CallToolResult
│   │   │   ├── HttpRequestBuilder.cs       # Path params → URL, query params → query string, body → request body
│   │   │   ├── ResponseWrapper.cs          # HTTP response → MCP CallToolResult (truncated, isError for non-2xx)
│   │   │   └── MetaToolsHandler.cs         # For dynamic mode: list_api_endpoints, get_api_endpoint_schema, invoke_api_endpoint
│   │   ├── Auth/
│   │   │   ├── OboTokenExchange.cs         # Entra ID OBO flow — exchange caller token for API-scoped token
│   │   │   ├── OboTokenCache.cs            # In-memory LRU cache for exchanged tokens (TTL-based)
│   │   │   ├── AuthStrategyResolver.cs     # Resolve which auth strategy per server definition
│   │   │   └── AuthDelegatingHandler.cs    # DelegatingHandler for HttpClient — injects auth header per strategy
│   │   ├── Audit/
│   │   │   ├── AuditEvent.cs               # Audit record schema
│   │   │   ├── QueueEmitter.cs             # Fire-and-forget to Azure Storage Queue
│   │   │   └── DiskFallback.cs            # Local disk buffer if queue is down + retry worker
│   │   ├── SpecManagement/
│   │   │   ├── SpecDiffService.cs          # Diff old vs new OpenAPI spec (added/removed/changed tools)
│   │   │   ├── SpecFetcher.cs             # Fetch spec from URL or file
│   │   │   └── SpecRefresher.cs            # Manual refresh + polling background service (24h default)
│   │   └── Health/
│   │       └── DependencyHealthChecker.cs  # Background service: check PG + Storage Queue connectivity for /ready
│   │
│   ├── McpGateway.Persistence/            # EF Core + PostgreSQL
│   │   ├── McpGateway.Persistence.csproj
│   │   ├── McpGatewayDbContext.cs          # EF Core DbContext
│   │   ├── Entities/
│   │   │   ├── McpServerDefinitionEntity.cs
│   │   │   ├── ToolEntity.cs
│   │   │   ├── GatewayApiKeyEntity.cs
│   │   │   ├── ToolOverrideEntity.cs       # Admin tool description overrides (survives spec refresh)
│   │   │   └── SpecVersionEntity.cs        # Spec snapshot history
│   │   ├── Migrations/                     # EF Core migrations (code-first)
│   │   │   ├── 20260701_InitialCreate.cs
│   │   │   └── 20260701_InitialCreate.Designer.cs
│   │   └── Repositories/
│   │       ├── IServerDefinitionRepository.cs
│   │       ├── ServerDefinitionRepository.cs
│   │       ├── IGatewayApiKeyRepository.cs
│   │       ├── GatewayApiKeyRepository.cs
│   │       ├── IToolOverrideRepository.cs
│   │       └── ToolOverrideRepository.cs
│   │
│   ├── McpGateway.McpSdk/                 # Wrapper over ModelContextProtocol.AspNetCore SDK
│   │   ├── McpGateway.McpSdk.csproj
│   │   ├── DynamicToolProvider.cs          # Custom IMcpServerTool implementation — delegates to IToolStore
│   │   ├── DynamicMcpServerOptions.cs      # Configures MCP server options to use DynamicToolProvider
│   │   └── McpEndpointMapper.cs            # Maps /mcp/{server_name} endpoints with per-server tool set
│   │
│   ├── McpGateway.Management/             # Management API logic (used by AdminEndpoints)
│   │   ├── McpGateway.Management.csproj
│   │   ├── Services/
│   │   │   ├── ServerManagementService.cs  # Register, list, get, update, delete, refresh, approve
│   │   │   ├── ToolManagementService.cs    # List tools, update override, toggle visibility (curated mode)
│   │   │   ├── GatewayApiKeyService.cs    # Issue, revoke, scope API keys per server
│   │   │   └── ClientProfileService.cs    # Set client profile per server
│   │   └── Contracts/
│   │       ├── Dtos.cs                     # Request/response DTOs for management API
│   │       └── Validators.cs              # FluentValidation validators for DTOs
│   │
│   └── McpGateway.Telemetry/             # OpenTelemetry setup
│       ├── McpGateway.Telemetry.csproj
│       ├── TelemetrySetup.cs              # OTLP exporter config, span helpers, metric definitions
│       └── ActivitySources.cs            # ActivitySource definitions: "McpGateway.ToolCall", "McpGateway.OboExchange"
│
├── tests/
│   ├── McpGateway.UnitTests/              # xUnit — mirrors src/ structure
│   │   ├── McpGateway.UnitTests.csproj
│   │   ├── ToolGeneration/
│   │   │   ├── OpenApiParserTests.cs
│   │   │   ├── ToolGeneratorTests.cs
│   │   │   ├── SchemaTransformerTests.cs
│   │   │   └── ToolNameResolverTests.cs
│   │   ├── Proxy/
│   │   │   ├── ToolCallHandlerTests.cs
│   │   │   ├── HttpRequestBuilderTests.cs
│   │   │   └── ResponseWrapperTests.cs
│   │   ├── Auth/
│   │   │   ├── OboTokenExchangeTests.cs
│   │   │   └── AuthStrategyResolverTests.cs
│   │   ├── ToolStore/
│   │   │   └── InMemoryToolStoreTests.cs
│   │   └── SpecManagement/
│   │       └── SpecDiffServiceTests.cs
│   │
│   ├── McpGateway.IntegrationTests/       # xUnit + Testcontainers (PG, Azurite, Jaeger)
│   │   ├── McpGateway.IntegrationTests.csproj
│   │   ├── McpEndpointTests.cs            # Full MCP tools/list + tools/call flow via HTTP
│   │   ├── AdminApiTests.cs               # Management API CRUD
│   │   ├── SpecRefreshTests.cs            # Spec change → diff → hot-reload → tools/list reflects change
│   │   ├── OboAuthTests.cs                # OBO token exchange end-to-end (mocked Entra ID)
│   │   └── AuditTests.cs                  # Audit event emission to Azurite queue
│   │
│   └── McpGateway.BddTests/              # xUnit + Gherkin (SpecFlow or Reqnroll)
│       ├── McpGateway.BddTests.csproj
│       ├── Features/
│       │   ├── ToolGeneration.feature
│       │   ├── ToolCallProxy.feature
│       │   ├── SpecRefresh.feature
│       │   ├── AuthStrategies.feature
│       │   └── ClientProfiles.feature
│       └── Steps/
│           ├── ToolGenerationSteps.cs
│           ├── ToolCallProxySteps.cs
│           └── ...
│
├── scripts/
│   └── seed-dev-data.sh                  # Populate dev PG with test API specs + definitions
│
├── docker/
│   └── Dockerfile                         # Multi-stage build (dotnet sdk → aspnet runtime)
│
├── docker-compose.yml                     # gateway, postgres, azurite, jaeger
├── McpGateway.sln                         # Solution file
└── docs/
    ├── CONTEXT.md
    ├── deep-research.md
    ├── project-structure.md
    ├── data-model.md
    ├── api-specification.md
    ├── testing-strategy.md
    └── adr/
        ├── 0001-httpclient-polly-for-tool-proxying.md
        ├── 0002-auth-injection-three-strategies-obo-default.md
        ├── 0003-hot-reload-tool-registration.md
        └── 0004-migration-sql-script-for-dba.md
```

## Key conventions

1. **Project separation** — 6 C# projects in 3 layers:
   - `McpGateway.Api` — HTTP endpoints, auth middleware (depends on all)
   - `McpGateway.Core` — domain logic, no HTTP deps (depends on Persistence interfaces)
   - `McpGateway.Persistence` — EF Core + PG (depends on Core entities)
   - `McpGateway.McpSdk` — wrapper over C# MCP SDK (depends on Core)
   - `McpGateway.Management` — management API logic (depends on Core + Persistence)
   - `McpGateway.Telemetry` — OTLP setup (depends on nothing, referenced by Api)

2. **No circular deps.** Core depends on nothing (pure domain). Persistence implements Core interfaces. Api references everything. McpSdk wraps the SDK and delegates to Core's IToolStore.

3. **One file = one class.** `OboTokenExchange.cs` does OBO exchange. Nothing else. If a class needs a second responsibility, split it.

4. **Endpoints are minimal APIs.** `McpEndpoints.cs` maps `/mcp/{server_name}`. `AdminEndpoints.cs` maps `/admin/*`. `HealthEndpoints.cs` maps `/health` and `/ready`. Reading `Program.cs` top-to-bottom shows the full middleware pipeline.

5. **ToolStore is the hot-reload mechanism.** `InMemoryToolStore` (ConcurrentDictionary) is the runtime truth. PG is the persistent truth. On startup, PG → InMemory. On refresh, PG updated → InMemory updated → next `tools/list` reflects changes. No restart needed.

6. **HttpClient per registered API.** Named clients via IHttpClientFactory. Polly policies (retry, circuit breaker, timeout) configured per named client. Auth injection via DelegatingHandler per client.

7. **Tests mirror src.** Unit tests in `McpGateway.UnitTests/` mirror `McpGateway.Core/` namespaces. Integration tests use Testcontainers (PG, Azurite, Jaeger). BDD tests use Gherkin features for business scenarios.

## .NET BDD framework note

For .NET, the Gherkin/BDD ecosystem is:
- **Reqnroll** — the successor to SpecFlow (open-source, actively maintained, .NET-native)
- **SpecFlow** — was the standard but is in maintenance mode (end-of-life 2024)
- **xUnit** as the test runner underneath

Recommendation: **Reqnroll + xUnit**. Same pattern as Gateway Zero's @amiceli/vitest-cucumber — Gherkin .feature files for high-level business scenarios only. Unit and integration tests stay plain xUnit.