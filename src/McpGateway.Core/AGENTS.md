# `McpGateway.Core` — Agent Context

## Scope

This project is the **domain heart** of the gateway. It owns the `McpServerDefinition` aggregate, OpenAPI→MCP tool generation, the in-memory `IToolStore`, the proxy call pipeline, auth orchestration (OBO/passthrough/static), audit emission, and health/shutdown plumbing.

It deliberately has **no ASP.NET Core endpoint wiring**, **no EF Core**, and **no MCP SDK references**. Its only sibling dependency is `McpGateway.Telemetry`.

## Public Extension Entrypoints

| Method | File | Registers |
|---|---|---|
| `AddMcpCore()` | `CoreServiceExtensions.cs` | `IToolStore`, `ToolStoreInitializer`, `ISpecFetcher`, `IToolGenerator`, `ISpecDiffService`, `ISpecRefresher` + hosted `SpecRefresher` |
| `AddMcpProxy()` | `Proxy/ProxyServiceExtensions.cs` | `ToolCallHandler`, `HttpRequestBuilder`, `ResponseWrapper`, named `HttpClient` (`McpToolProxy`) with Polly retry/circuit-breaker, OBO exchange/cache, `AuthDelegatingHandler` |
| `AddMcpAudit()` | `Audit/AuditServiceExtensions.cs` | `IAuditEmitter` (`QueueEmitter`), `DiskFallback`, `DiskFallbackRetryWorker` |
| `AddMcpHealth()` | `Health/CoreHealthServiceExtensions.cs` | readiness state, dependency probes, in-flight tracker, `DependencyHealthChecker`, `GracefulShutdownService` |

## Key Domain Types

- `ServerDefinitions/McpServerDefinition.cs` — aggregate root. Name, base URL, auth strategy, approval status, tool mode, client profile, collections of tools / overrides / API keys / spec versions.
- `ServerDefinitions/ToolDefinition.cs` — one MCP tool from `(path, method)`.
- `ToolStore/IToolStore.cs` / `InMemoryToolStore.cs` — hot-reloadable store used by `McpSdk` and `Proxy`.
- `Repositories/*` — interfaces implemented by `McpGateway.Persistence`.

## Subsystem Map

```
SpecManagement/    → fetch specs, diff, refresh, ServerSpecRefresher, SpecRefresher hosted service
ToolGeneration/    → parse OpenAPI, build tools, apply client-profile schema transforms
Proxy/             → ToolCallHandler, HttpRequestBuilder, ResponseWrapper, MetaToolsHandler, ToolCallContext
Auth/              → CallerIdentity, OBO exchange/cache, AuthStrategyResolver, AuthDelegatingHandler
Audit/             → QueueEmitter + disk fallback + retry worker
Health/            → readiness probes, in-flight tracker, graceful shutdown
```

## Rules for This Directory

- Keep **domain exceptions** under `*.Exceptions` namespaces (e.g., `Proxy/Exceptions/ToolNotFoundException.cs`).
- Forward `CancellationToken` through every `async` method.
- Use `TimeProvider` and `HttpMessageHandler` abstractions for testability.
- No `ConfigureAwait(false)` in ASP.NET Core/test-adjacent code.
- Telemetry is emitted via `ActivitySources` and `TelemetryMetrics` from `McpGateway.Telemetry`.
- `IToolStore` is **write-on-approve-only**: `McpGateway.Management` is the only writer; this project reads.
