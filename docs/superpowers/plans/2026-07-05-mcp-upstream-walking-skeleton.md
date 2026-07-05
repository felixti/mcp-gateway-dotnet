# mcp-upstream Source Type — Walking Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second source type (`mcp-upstream`) so the gateway can ingest an already-existing remote MCP server's tool catalog and proxy `tools/call` to it — validated end-to-end against an in-process MCP test server using existing (static) auth.

**Architecture:** `McpServerDefinition` gains a `SourceType` discriminator (`openapi | mcp-upstream`). Tool invocation is extracted behind an `IToolInvocationStrategy` polymorphism (`HttpInvocationStrategy` for openapi — unchanged behavior; `McpUpstreamInvocationStrategy` for the new path). The upstream client wraps the `ModelContextProtocol` SDK's `McpClient` over `StreamableHttp`. Registration branches on source type: openapi parses a spec (status quo), mcp-upstream imports the upstream's `tools/list`. Approval, audit, telemetry, and the tool store are reused unchanged.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 (PostgreSQL, SQL-scripted migrations per ADR-0004), `ModelContextProtocol.AspNetCore` 2.0.0-preview.1 (used both server- and client-side), xUnit, WebApplicationFactory.

**Reference:** `docs/adr/0007-source-types-and-auth-strategy-matrix.md`. This plan implements only ADR-0007 §1 (discriminator), §2 (re-host semantics), and the SDK-client wiring. Auth-strategy refactor (§3), refresh-diff, Propagated Principal, and sanitization are deferred to Plan 2.

---

## Scope boundaries (what this plan does NOT do)

- No auth-strategy refactor (`obo`/`credential`). mcp-upstream uses a plain (unauthenticated) upstream connection in tests. Auth injection for mcp-upstream is Plan 2.
- No refresh / catalog diff for mcp-upstream. Re-registration is manual in this plan.
- No Propagated Principal / `Caller.PrincipalType` change.
- No third-party sanitization.
- These deferrals are explicit; ADR-0007 forbids third-party mcp-upstream in production until Plan 2 + sanitization land.

## File Structure

**Create:**
- `src/McpGateway.Core/ServerDefinitions/SourceType.cs` — enum `OpenApi | McpUpstream`
- `src/McpGateway.Core/Proxy/IToolInvocationStrategy.cs` — strategy contract
- `src/McpGateway.Core/Proxy/HttpInvocationStrategy.cs` — extracted HTTP proxy logic (behavior-identical to today's `ToolCallHandler`)
- `src/McpGateway.Core/Proxy/McpUpstreamInvocationStrategy.cs` — forwards `tools/call` via `IMcpUpstreamClient`
- `src/McpGateway.Core/McpUpstream/UpstreamTool.cs` — record catalog import DTO
- `src/McpGateway.Core/McpUpstream/IMcpUpstreamClient.cs` — `ListToolsAsync` / `CallToolAsync`
- `src/McpGateway.Core/McpUpstream/SdkMcpUpstreamClient.cs` — SDK wrapper
- `src/McpGateway.Core/McpUpstream/UpstreamCatalogImporter.cs` — maps `UpstreamTool` → `ToolDefinition`
- `tests/McpGateway.IntegrationTests/McpUpstream/McpUpstreamIntegrationTests.cs` — end-to-end against an in-proc upstream
- `src/McpGateway.Persistence/Migrations/V8__add_source_type.sql` (follow existing migration numbering)

**Modify:**
- `src/McpGateway.Core/ServerDefinitions/ToolDefinition.cs` — `HttpMethod`/`HttpPath` become nullable
- `src/McpGateway.Core/ServerDefinitions/McpServerDefinition.cs` — add `SourceType` property
- `src/McpGateway.Core/Proxy/ToolCallHandler.cs` — dispatch via `IToolInvocationStrategy` resolved by `SourceType`
- `src/McpGateway.Core/CoreServiceExtensions.cs` — register strategy + upstream client
- `src/McpGateway.Management/Services/ServerManagementService.cs` — `RegisterAsync` branches on source type
- `src/McpGateway.Management/Contracts/` (CreateServerRequest + ServerResponse) — add `SourceType`, `UpstreamUrl`
- `src/McpGateway.Persistence/` entity configuration — `SourceType` column + nullable tool HTTP coords
- `src/McpGateway.Core/Proxy/HttpRequestBuilder.cs` — null-guard (HttpPath now nullable)

---

## Task 1: SDK client API spike (de-risk)

**Files:**
- Test: `tests/McpGateway.UnitTests/McpUpstream/SdkClientApiSmokeTest.cs`

The SDK is `2.0.0-preview.1`; community docs show `ModelContextProtocol.Client.McpClient.CreateAsync(HttpClientTransport)`. Verify the exact surface compiles against the referenced package before building on it.

- [ ] **Step 1: Write a compile-and-run smoke test**

```csharp
namespace McpGateway.UnitTests.McpUpstream;

public class SdkClientApiSmokeTest
{
    [Fact]
    public void Client_transport_types_are_reachable()
    {
        // If any of these fail to compile, the preview API surface differs
        // from the community docs — adjust SdkMcpUpstreamClient accordingly
        // before proceeding to Task 7.
        _ = typeof(ModelContextProtocol.Client.McpClient);
        _ = typeof(ModelContextProtocol.Protocol.HttpClientTransport);
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~SdkClientApiSmokeTest"`
Expected: PASS. If FAIL, inspect the package with `dotnet build` intellisense / the SDK source to find the real client + transport type names, record the correction in this task's notes, and proceed.

- [ ] **Step 3: Commit**

```bash
git add tests/McpGateway.UnitTests/McpUpstream/SdkClientApiSmokeTest.cs
git commit -m "test: spike MCP SDK client API surface for mcp-upstream"
```

---

## Task 2: SourceType enum + discriminator on McpServerDefinition

**Files:**
- Create: `src/McpGateway.Core/ServerDefinitions/SourceType.cs`
- Modify: `src/McpGateway.Core/ServerDefinitions/McpServerDefinition.cs`
- Test: `tests/McpGateway.UnitTests/ServerDefinitions/SourceTypeTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.UnitTests.ServerDefinitions;

public class SourceTypeTests
{
    [Fact]
    public void McpServerDefinition_defaults_to_openapi_source_type()
    {
        var def = new McpServerDefinition();
        Assert.Equal(SourceType.OpenApi, def.SourceType);
    }

    [Theory]
    [InlineData(SourceType.OpenApi, "openapi")]
    [InlineData(SourceType.McpUpstream, "mcp-upstream")]
    public void SourceType_maps_to_canonical_string(SourceType type, string expected)
    {
        Assert.Equal(expected, type.ToCanonicalString());
    }
}
```

- [ ] **Step 2: Run — expect compile failure (`SourceType` / `ToCanonicalString` undefined)**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~SourceTypeTests"`
Expected: FAIL (compile error).

- [ ] **Step 3: Create the enum**

`src/McpGateway.Core/ServerDefinitions/SourceType.cs`:
```csharp
using System.Text.Json.Serialization;

namespace McpGateway.Core.ServerDefinitions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceType
{
    OpenApi,
    McpUpstream
}

public static class SourceTypeExtensions
{
    public static string ToCanonicalString(this SourceType type) => type switch
    {
        SourceType.OpenApi => "openapi",
        SourceType.McpUpstream => "mcp-upstream",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
```

- [ ] **Step 4: Add the property to McpServerDefinition**

Add to `McpServerDefinition.cs` (after `ClientProfile`):
```csharp
    public SourceType SourceType { get; set; } = SourceType.OpenApi;
```

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~SourceTypeTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Core/ServerDefinitions/SourceType.cs \
        src/McpGateway.Core/ServerDefinitions/McpServerDefinition.cs \
        tests/McpGateway.UnitTests/ServerDefinitions/SourceTypeTests.cs
git commit -m "feat: add SourceType discriminator to McpServerDefinition"
```

---

## Task 3: Make ToolDefinition HttpMethod/HttpPath nullable

**Files:**
- Modify: `src/McpGateway.Core/ServerDefinitions/ToolDefinition.cs:9-10`
- Modify: `src/McpGateway.Core/Proxy/HttpRequestBuilder.cs:9-15`
- Test: `tests/McpGateway.UnitTests/Proxy/HttpRequestBuilderTests.cs` (extend)

- [ ] **Step 1: Write a failing test for null-tolerant HttpRequestBuilder**

Add to `HttpRequestBuilderTests.cs`:
```csharp
[Fact]
public void Build_throws_when_tool_has_no_http_coordinates()
{
    var builder = new HttpRequestBuilder();
    var tool = new ToolDefinition { HttpMethod = null!, HttpPath = null! };
    Assert.Throws<InvalidOperationException>(() =>
        builder.Build("https://api.example.com", tool, new Dictionary<string, object?>()));
}
```

- [ ] **Step 2: Run — expect FAIL (no exception thrown today; non-null strings)**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~HttpRequestBuilderTests"`
Expected: FAIL.

- [ ] **Step 3: Make the properties nullable + guard Build**

In `ToolDefinition.cs` change:
```csharp
    public string? HttpMethod { get; set; }
    public string? HttpPath { get; set; }
```

In `HttpRequestBuilder.Build`, add a guard as the first line:
```csharp
    public HttpRequestMessage Build(string baseUrl, ToolDefinition tool, IReadOnlyDictionary<string, object?> arguments)
    {
        if (string.IsNullOrWhiteSpace(tool.HttpPath) || string.IsNullOrWhiteSpace(tool.HttpMethod))
        {
            throw new InvalidOperationException(
                $"Tool '{tool.ToolName}' has no HTTP coordinates; it is not an OpenAPI-backed tool.");
        }
        // ... existing body unchanged ...
    }
```

- [ ] **Step 4: Run — expect PASS (full HttpRequestBuilderTests suite)**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~HttpRequestBuilderTests"`
Expected: PASS.

- [ ] **Step 5: Run whole solution build to catch nullability fallout**

Run: `dotnet build McpGateway.sln`
Expected: SUCCESS, 0 errors. Fix any new nullable warnings in OpenAPI tool-generation paths (they always set HttpMethod/HttpPath, so should be clean).

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Core/ServerDefinitions/ToolDefinition.cs \
        src/McpGateway.Core/Proxy/HttpRequestBuilder.cs \
        tests/McpGateway.UnitTests/Proxy/HttpRequestBuilderTests.cs
git commit -m "refactor: make ToolDefinition HTTP coords nullable for non-HTTP tools"
```

---

## Task 4: IToolInvocationStrategy + HttpInvocationStrategy (behavior-preserving extraction)

**Files:**
- Create: `src/McpGateway.Core/Proxy/IToolInvocationStrategy.cs`
- Create: `src/McpGateway.Core/Proxy/HttpInvocationStrategy.cs`
- Test: `tests/McpGateway.UnitTests/Proxy/HttpInvocationStrategyTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using System.Text;
using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.UnitTests.Proxy;

public class HttpInvocationStrategyTests
{
    [Fact]
    public async Task Invoke_builds_request_and_wraps_response()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("hello", Encoding.UTF8) });
        var http = new HttpClient(handler);
        var strategy = new HttpInvocationStrategy(
            new HttpRequestBuilder(), new ResponseWrapper(), http);

        var server = new McpServerDefinition { BaseUrl = "https://api.example.com", SourceType = SourceType.OpenApi };
        var tool = new ToolDefinition { HttpMethod = "GET", HttpPath = "/items" };

        var result = await strategy.InvokeAsync(server, tool, new Dictionary<string, object?>(), default);

        Assert.False(result.IsError);
        Assert.Equal("hello", result.Content[0].Text);
        Assert.Equal(SourceType.OpenApi, strategy.SourceType);
        Assert.Contains("GET", handler.LastRequest?.Method.ToString());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;
        public HttpRequestMessage? LastRequest { get; private set; }
        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { LastRequest = r; return Task.FromResult(_respond()); }
    }
}
```

- [ ] **Step 2: Run — expect compile FAIL**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~HttpInvocationStrategyTests"`
Expected: FAIL (types missing).

- [ ] **Step 3: Create the interface**

`src/McpGateway.Core/Proxy/IToolInvocationStrategy.cs`:
```csharp
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Proxy;

public interface IToolInvocationStrategy
{
    SourceType SourceType { get; }
    Task<ToolCallResult> InvokeAsync(
        McpServerDefinition server,
        ToolDefinition tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Create HttpInvocationStrategy (extracted from ToolCallHandler)**

`src/McpGateway.Core/Proxy/HttpInvocationStrategy.cs`:
```csharp
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Proxy;

public sealed class HttpInvocationStrategy : IToolInvocationStrategy
{
    private readonly HttpRequestBuilder _requestBuilder;
    private readonly ResponseWrapper _responseWrapper;
    private readonly HttpClient _httpClient;

    public HttpInvocationStrategy(
        HttpRequestBuilder requestBuilder,
        ResponseWrapper responseWrapper,
        HttpClient httpClient)
    {
        _requestBuilder = requestBuilder;
        _responseWrapper = responseWrapper;
        _httpClient = httpClient;
    }

    public SourceType SourceType => SourceType.OpenApi;

    public async Task<ToolCallResult> InvokeAsync(
        McpServerDefinition server,
        ToolDefinition tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        var request = _requestBuilder.Build(server.BaseUrl, tool, arguments);
        var response = await _httpClient.SendAsync(request, ct);
        return await _responseWrapper.WrapAsync(response);
    }
}
```

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~HttpInvocationStrategyTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Core/Proxy/IToolInvocationStrategy.cs \
        src/McpGateway.Core/Proxy/HttpInvocationStrategy.cs \
        tests/McpGateway.UnitTests/Proxy/HttpInvocationStrategyTests.cs
git commit -m "feat: extract HTTP tool invocation behind IToolInvocationStrategy"
```

---

## Task 5: Refactor ToolCallHandler to dispatch via strategy

**Files:**
- Modify: `src/McpGateway.Core/Proxy/ToolCallHandler.cs`
- Modify: `src/McpGateway.Core/Proxy/ProxyServiceExtensions.cs` (DI registration lives here per codegraph)
- Test: `tests/McpGateway.UnitTests/Proxy/ToolCallHandlerTests.cs` (ensure existing tests still pass)

- [ ] **Step 1: Confirm existing ToolCallHandlerTests are green before refactor**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~ToolCallHandlerTests"`
Expected: PASS (baseline). Record the count.

- [ ] **Step 2: Refactor ToolCallHandler — replace inline HTTP build/send/wrap with strategy dispatch**

Replace the body of `HandleAsync` between resolving `tool` and computing `elapsed`:

```csharp
// REMOVE these three lines:
// var request = _requestBuilder.Build(server.BaseUrl, tool, arguments);
// var response = await _httpClient.SendAsync(request, ct);
// var result = await _responseWrapper.WrapAsync(response);
//
// REPLACE with:
var strategy = _strategies.FirstOrDefault(s => s.SourceType == server.SourceType)
    ?? throw new InvalidOperationException(
        $"No invocation strategy registered for source type '{server.SourceType}'.");
var response = await strategy.InvokeAsync(server, tool, arguments, ct);
// NOTE: HttpInvocationStrategy returns a ToolCallResult directly. To preserve the
// telemetry tags that read `response.StatusCode`, restructure as below.
```

Concretely, change `HandleAsync` so the strategy call replaces the request/response/wrap block, and derive `IsError`/status from the returned `ToolCallResult`. Add a private field:
```csharp
    private readonly IReadOnlyCollection<IToolInvocationStrategy> _strategies;
```
and inject `IEnumerable<IToolInvocationStrategy> strategies` in the constructor, assigning `_strategies = strategies.ToList()`. Remove the now-unused `_requestBuilder`, `_responseWrapper`, `_httpClient` fields and their constructor params (HttpClient is owned by HttpInvocationStrategy now). Adapt the telemetry block: the `response.StatusCode` tag becomes the strategy's responsibility to surface — for the skeleton, set the `mcp.tool.http_status` tag only when available; introduce a small `ToolCallResult.HttpStatus` nullable field set by `HttpInvocationStrategy.WrapAsync` path (or drop that tag for the skeleton and re-add in Plan 2 telemetry hardening). Keep `IsError` from `result.IsError`.

Because telemetry status-tag handling changes, **audit the existing ToolCallHandlerTests** and update any assertion on `mcp.tool.http_status` to match (the openapi path still surfaces it via HttpInvocationStrategy returning a result that carries status — simplest: keep status on `ToolCallResult`).

- [ ] **Step 3: Register HttpInvocationStrategy in DI**

In `ProxyServiceExtensions.cs` (where `ToolCallHandler` is registered), add:
```csharp
    services.AddSingleton<HttpRequestBuilder>();
    services.AddSingleton<ResponseWrapper>();
    services.AddHttpClient<HttpInvocationStrategy>(ToolCallHandler.HttpClientName);
    services.AddSingleton<IToolInvocationStrategy>(sp => sp.GetRequiredService<HttpInvocationStrategy>());
```
Keep the named HttpClient (`McpToolProxy`) so Polly/auth DelegatingHandler wiring is preserved on the openapi path.

- [ ] **Step 4: Run — existing ToolCallHandlerTests must still PASS**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~ToolCallHandlerTests"`
Expected: PASS (same count as Step 1). If a test asserts the removed `_httpClient` field via reflection, rewrite it to assert through the strategy.

- [ ] **Step 5: Commit**

```bash
git add src/McpGateway.Core/Proxy/ToolCallHandler.cs \
        src/McpGateway.Core/Proxy/ProxyServiceExtensions.cs \
        tests/McpGateway.UnitTests/Proxy/ToolCallHandlerTests.cs
git commit -m "refactor: ToolCallHandler dispatches via IToolInvocationStrategy"
```

---

## Task 6: UpstreamTool + UpstreamCatalogImporter

**Files:**
- Create: `src/McpGateway.Core/McpUpstream/UpstreamTool.cs`
- Create: `src/McpGateway.Core/McpUpstream/UpstreamCatalogImporter.cs`
- Test: `tests/McpGateway.UnitTests/McpUpstream/UpstreamCatalogImporterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json.Nodes;
using McpGateway.Core.McpUpstream;

namespace McpGateway.UnitTests.McpUpstream;

public class UpstreamCatalogImporterTests
{
    [Fact]
    public void Import_maps_upstream_tools_to_tool_definitions_without_http_coords()
    {
        var importer = new UpstreamCatalogImporter();
        var serverId = Guid.NewGuid();
        var upstream = new List<UpstreamTool>
        {
            new("search_kb", "Search the knowledge base", JsonNode.Parse("""{"type":"object"}""")!),
            new("echo", null, null)
        };

        var tools = importer.Import(upstream, serverId);

        Assert.Equal(2, tools.Count);
        Assert.All(tools, t => Assert.Equal(serverId, t.ServerDefinitionId));
        Assert.All(tools, t => { Assert.Null(t.HttpMethod); Assert.Null(t.HttpPath); });
        Assert.Equal("search_kb", tools[0].ToolName);
        Assert.Equal("Search the knowledge base", tools[0].Description);
        Assert.True(tools.All(t => t.Visible));
    }
}
```

- [ ] **Step 2: Run — expect compile FAIL**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~UpstreamCatalogImporterTests"`
Expected: FAIL.

- [ ] **Step 3: Create UpstreamTool + importer**

`src/McpGateway.Core/McpUpstream/UpstreamTool.cs`:
```csharp
using System.Text.Json.Nodes;

namespace McpGateway.Core.McpUpstream;

public sealed record UpstreamTool(string Name, string? Description, JsonNode? InputSchema);
```

`src/McpGateway.Core/McpUpstream/UpstreamCatalogImporter.cs`:
```csharp
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.McpUpstream;

public class UpstreamCatalogImporter
{
    public IReadOnlyList<ToolDefinition> Import(IReadOnlyList<UpstreamTool> tools, Guid serverId)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var now = DateTime.UtcNow;
        return tools.Select(t => new ToolDefinition
        {
            Id = Guid.NewGuid(),
            ServerDefinitionId = serverId,
            ToolName = t.Name,
            Description = t.Description ?? string.Empty,
            HttpMethod = null,
            HttpPath = null,
            InputSchema = t.InputSchema?.ToJsonString() ?? "{}",
            OutputSchema = null,
            AuthConfig = "{}",
            Visible = true,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
    }
}
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~UpstreamCatalogImporterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/McpGateway.Core/McpUpstream/UpstreamTool.cs \
        src/McpGateway.Core/McpUpstream/UpstreamCatalogImporter.cs \
        tests/McpGateway.UnitTests/McpUpstream/UpstreamCatalogImporterTests.cs
git commit -m "feat: upstream MCP catalog importer"
```

---

## Task 7: IMcpUpstreamClient + SdkMcpUpstreamClient

**Files:**
- Create: `src/McpGateway.Core/McpUpstream/IMcpUpstreamClient.cs`
- Create: `src/McpGateway.Core/McpUpstream/SdkMcpUpstreamClient.cs`
- Modify: `src/McpGateway.McpSdk/McpGateway.McpSdk.csproj` (if client types need a separate package ref — verify via Task 1)
- Test: `tests/McpGateway.UnitTests/McpUpstream/SdkMcpUpstreamClientTests.cs` (mock the SDK client via a seam)

- [ ] **Step 1: Define the interface (seam for testing)**

`src/McpGateway.Core/McpUpstream/IMcpUpstreamClient.cs`:
```csharp
using System.Text.Json.Nodes;

namespace McpGateway.Core.McpUpstream;

public interface IMcpUpstreamClient
{
    Task<IReadOnlyList<UpstreamTool>> ListToolsAsync(string endpoint, CancellationToken ct = default);
    Task<ToolCallResult> CallToolAsync(
        string endpoint, string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the failing test against the interface (fake upstream client)**

```csharp
using McpGateway.Core.McpUpstream;
using McpGateway.Core.Proxy;

namespace McpGateway.UnitTests.McpUpstream;

public class SdkMcpUpstreamClientTests
{
    [Fact]
    public async Task CallToolAsync_returns_text_content_from_upstream()
    {
        IMcpUpstreamClient client = new FakeUpstreamClient();
        var result = await client.CallToolAsync(
            "https://upstream.example.com/mcp", "echo",
            new Dictionary<string, object?> { ["msg"] = "hi" }, default);

        Assert.False(result.IsError);
        Assert.Equal("echo:hi", result.Content[0].Text);
    }

    private sealed class FakeUpstreamClient : IMcpUpstreamClient
    {
        public Task<IReadOnlyList<UpstreamTool>> ListToolsAsync(string endpoint, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UpstreamTool>>(new List<UpstreamTool>());
        public Task<ToolCallResult> CallToolAsync(string endpoint, string toolName,
            IReadOnlyDictionary<string, object?> arguments, CancellationToken ct = default)
            => Task.FromResult(new ToolCallResult
            {
                Content = new() { new() { Type = "text", Text = $"echo:{arguments["msg"]}" } }
            });
    }
}
```

- [ ] **Step 3: Run — expect PASS for the fake (proves the interface contract); SDK impl not yet exercised**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~SdkMcpUpstreamClientTests"`
Expected: PASS.

- [ ] **Step 4: Implement SdkMcpUpstreamClient using the real SDK**

> If Task 1 revealed the SDK surface differs from `McpClient`/`HttpClientTransport`, substitute the real type names here. Per the spike they should be `ModelContextProtocol.Client.McpClient` + `HttpClientTransport`.

`src/McpGateway.Core/McpUpstream/SdkMcpUpstreamClient.cs`:
```csharp
using System.Text.Json.Nodes;
using McpGateway.Core.Proxy;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpGateway.Core.McpUpstream;

public sealed class SdkMcpUpstreamClient : IMcpUpstreamClient
{
    public async Task<IReadOnlyList<UpstreamTool>> ListToolsAsync(string endpoint, CancellationToken ct = default)
    {
        await using var client = await ConnectAsync(endpoint, ct);
        var tools = await client.ListToolsAsync(ct);
        return tools.Select(t => new UpstreamTool(
            t.Name,
            t.Description,
            t.JsonSchema is null ? null : JsonNode.Parse(t.JsonSchema.Value.GetRawText()))).ToList();
    }

    public async Task<ToolCallResult> CallToolAsync(
        string endpoint, string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        await using var client = await ConnectAsync(endpoint, ct);
        var result = await client.CallToolAsync(toolName, arguments, ct);
        return new ToolCallResult
        {
            IsError = result.IsError,
            Content = result.Content.OfType<TextContentBlock>()
                .Select(b => new ToolCallContent { Type = "text", Text = b.Text })
                .ToList()
        };
    }

    private static async Task<IMcpClient> ConnectAsync(string endpoint, CancellationToken ct)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp
        });
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }
}
```

> **Note:** `IMcpClient`, `McpClientTool.JsonSchema`, and `TextContentBlock` field shapes vary across preview builds. If Step 4 doesn't compile, read the SDK's actual types (the Task 1 spike + IntelliSense) and adjust the mapping. This is the highest-churn file in the plan — keep it isolated so corrections are local.

- [ ] **Step 5: Build (the real SDK path compiles)**

Run: `dotnet build src/McpGateway.Core/McpGateway.Core.csproj`
Expected: SUCCESS. If the McpSdk project needs a `ModelContextProtocol.Client` package ref, add it to `McpGateway.Core.csproj` (the client lives in the Core project, not McpSdk).

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Core/McpUpstream/IMcpUpstreamClient.cs \
        src/McpGateway.Core/McpUpstream/SdkMcpUpstreamClient.cs \
        src/McpGateway.Core/McpGateway.Core.csproj \
        tests/McpGateway.UnitTests/McpUpstream/SdkMcpUpstreamClientTests.cs
git commit -m "feat: SDK-backed upstream MCP client (ListTools + CallTool)"
```

---

## Task 8: McpUpstreamInvocationStrategy + DI

**Files:**
- Create: `src/McpGateway.Core/Proxy/McpUpstreamInvocationStrategy.cs`
- Modify: `src/McpGateway.Core/CoreServiceExtensions.cs`
- Test: `tests/McpGateway.UnitTests/Proxy/McpUpstreamInvocationStrategyTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using McpGateway.Core.McpUpstream;
using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.UnitTests.Proxy;

public class McpUpstreamInvocationStrategyTests
{
    [Fact]
    public async Task Invoke_forwards_call_to_upstream_client()
    {
        var fake = new FakeClient();
        var strategy = new McpUpstreamInvocationStrategy(fake);
        var server = new McpServerDefinition { BaseUrl = "https://up/mcp", SourceType = SourceType.McpUpstream };
        var tool = new ToolDefinition { ToolName = "echo" };

        var result = await strategy.InvokeAsync(server, tool,
            new Dictionary<string, object?> { ["msg"] = "x" }, default);

        Assert.Equal("forwarded", result.Content[0].Text);
        Assert.Equal("https://up/mcp", fake.LastEndpoint);
        Assert.Equal("echo", fake.LastTool);
        Assert.Equal(SourceType.McpUpstream, strategy.SourceType);
    }

    private sealed class FakeClient : IMcpUpstreamClient
    {
        public string? LastEndpoint { get; private set; }
        public string? LastTool { get; private set; }
        public Task<IReadOnlyList<UpstreamTool>> ListToolsAsync(string endpoint, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UpstreamTool>>(new List<UpstreamTool>());
        public Task<ToolCallResult> CallToolAsync(string endpoint, string toolName,
            IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
        { LastEndpoint = endpoint; LastTool = toolName;
          return Task.FromResult(new ToolCallResult { Content = new() { new() { Type = "text", Text = "forwarded" } } }); }
    }
}
```

- [ ] **Step 2: Run — expect compile FAIL**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~McpUpstreamInvocationStrategyTests"`
Expected: FAIL.

- [ ] **Step 3: Create the strategy**

`src/McpGateway.Core/Proxy/McpUpstreamInvocationStrategy.cs`:
```csharp
using McpGateway.Core.McpUpstream;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Proxy;

public sealed class McpUpstreamInvocationStrategy : IToolInvocationStrategy
{
    private readonly IMcpUpstreamClient _client;

    public McpUpstreamInvocationStrategy(IMcpUpstreamClient client) => _client = client;

    public SourceType SourceType => SourceType.McpUpstream;

    public Task<ToolCallResult> InvokeAsync(
        McpServerDefinition server,
        ToolDefinition tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
        => _client.CallToolAsync(server.BaseUrl, tool.ToolName, arguments, ct);
}
```

- [ ] **Step 4: Register both strategies + client in DI**

In `CoreServiceExtensions.AddMcpCore`:
```csharp
    services.AddSingleton<UpstreamCatalogImporter>();
    services.AddSingleton<IMcpUpstreamClient, SdkMcpUpstreamClient>();
    services.AddSingleton<McpUpstreamInvocationStrategy>();
    services.AddSingleton<IToolInvocationStrategy>(sp => sp.GetRequiredService<McpUpstreamInvocationStrategy>());
```
(HttpInvocationStrategy registration lives in `ProxyServiceExtensions` per Task 5.)

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~McpUpstreamInvocationStrategyTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Core/Proxy/McpUpstreamInvocationStrategy.cs \
        src/McpGateway.Core/CoreServiceExtensions.cs \
        tests/McpGateway.UnitTests/Proxy/McpUpstreamInvocationStrategyTests.cs
git commit -m "feat: McpUpstreamInvocationStrategy + DI wiring"
```

---

## Task 9: RegisterAsync branches on SourceType (contracts + service)

**Files:**
- Modify: `src/McpGateway.Management/Contracts/CreateServerRequest.cs` (+ `ServerResponse.cs`)
- Modify: `src/McpGateway.Management/Services/ServerManagementService.cs:50-106`
- Test: `tests/McpGateway.UnitTests/Management/ServerManagementServiceTests.cs` (extend)

- [ ] **Step 1: Extend contracts**

Add to `CreateServerRequest`:
```csharp
    public string? SourceType { get; set; } // "openapi" (default) | "mcp-upstream"
    public string? UpstreamUrl { get; set; }
```
Add the same two fields to `ServerResponse` (DTO out), and surface `SourceType` in `ServerResponse.FromDomain`.

- [ ] **Step 2: Write a failing test for mcp-upstream registration (fake upstream client in the service ctor)**

Constructor-inject `IMcpUpstreamClient` + `UpstreamCatalogImporter` into `ServerManagementService`. Test:
```csharp
[Fact]
public async Task RegisterAsync_mcp_upstream_imports_catalog_without_openapi_parse()
{
    // arrange: repo stub returning null on GetByNameAsync, fake upstream client
    //          returning one UpstreamTool "echo", importer
    var request = new CreateServerRequest
    {
        Name = "echo-srv", DisplayName = "Echo", SourceType = "mcp-upstream",
        UpstreamUrl = "https://up.example.com/mcp", AuthStrategy = "static"
    };

    var saved = await _service.RegisterAsync(request, default);

    Assert.Equal(SourceType.McpUpstream, saved.SourceType);
    Assert.Single(saved.Tools);                      // mapping verified at repo save
    Assert.All(saved.Tools, t => { Assert.Null(t.HttpMethod); Assert.Null(t.HttpPath); });
}
```
Inject `IMcpUpstreamClient` as a fake in the test's service instance; the repo captures the definition passed to `AddAsync`.

- [ ] **Step 3: Run — expect FAIL**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~ServerManagementServiceTests"`
Expected: FAIL (SourceType branch absent).

- [ ] **Step 4: Add the branch to RegisterAsync**

At the top of `RegisterAsync`, after the existing-conflict check, branch:
```csharp
    var sourceType = string.IsNullOrWhiteSpace(request.SourceType)
        ? SourceType.OpenApi
        : Enum.Parse<SourceType>(Capitalize(request.SourceType));

    if (sourceType == SourceType.McpUpstream)
    {
        if (string.IsNullOrWhiteSpace(request.UpstreamUrl))
            throw new ValidationException(new[] { new FluentValidation.Results.ValidationFailure(
                nameof(request.UpstreamUrl), "UpstreamUrl is required for mcp-upstream source type.") });

        var upstreamTools = await _upstreamClient.ListToolsAsync(request.UpstreamUrl, ct);
        var tools = _catalogImporter.Import(upstreamTools, Guid.Empty); // Id set below

        var definition = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            SourceType = SourceType.McpUpstream,
            BaseUrl = request.UpstreamUrl.TrimEnd('/'),
            SpecSourceUrl = request.UpstreamUrl,
            SpecContent = "{}",
            SpecHash = ComputeHash("{}"),
            AuthStrategy = request.AuthStrategy,
            AuthConfig = request.AuthConfig.ToJsonString(),
            ToolMode = ToolMode.All,
            ClientProfile = ClientProfile.Universal,
            PollIntervalMinutes = request.PollIntervalMinutes,
            Status = "active",
            ApprovalStatus = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Tools = tools.Select(t => { t.ServerDefinitionId = Guid.Empty; return t; }).ToList()
        };
        // re-key tool ServerDefinitionId after Id is known:
        foreach (var t in definition.Tools) t.ServerDefinitionId = definition.Id;

        var saved = await _serverRepo.AddAsync(definition, ct);
        return ServerResponse.FromDomain(saved);
    }

    // ... existing openapi path unchanged ...
```
Add `IMcpUpstreamClient _upstreamClient` + `UpstreamCatalogImporter _catalogImporter` as constructor params + fields. Update all `ServerManagementService` tests' construction accordingly.

- [ ] **Step 5: Run — expect PASS**

Run: `dotnet test tests/McpGateway.UnitTests --filter "FullyQualifiedName~ServerManagementServiceTests"`
Expected: PASS (existing + new test).

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Management/Contracts/CreateServerRequest.cs \
        src/McpGateway.Management/Contracts/ServerResponse.cs \
        src/McpGateway.Management/Services/ServerManagementService.cs \
        tests/McpGateway.UnitTests/Management/ServerManagementServiceTests.cs
git commit -m "feat: register mcp-upstream servers (catalog import, no OpenAPI parse)"
```

---

## Task 10: EF Core mapping + SQL migration

**Files:**
- Modify: `src/McpGateway.Persistence/` (entity config for `McpServerDefinition` + `ToolDefinition`)
- Create: `src/McpGateway.Persistence/Migrations/V8__add_source_type.sql` (follow existing migration numbering — confirm next number)
- Test: `tests/McpGateway.IntegrationTests/Persistence/DbContextTests.cs` (extend)

- [ ] **Step 1: Add a failing integration test asserting a mcp-upstream row round-trips**

In `DbContextTests.cs`:
```csharp
[Fact]
public async Task Persists_mcp_upstream_definition_with_null_http_tool_coords()
{
    var def = new McpServerDefinition { /* ... minimal ... */ SourceType = SourceType.McpUpstream, BaseUrl = "https://up/mcp" };
    def.Tools.Add(new ToolDefinition { ToolName = "echo", HttpMethod = null, HttpPath = null, InputSchema = "{}" });
    // ... add + save + reload ...
    Assert.Equal(SourceType.McpUpstream, reloaded.SourceType);
    Assert.All(reloaded.Tools, t => { Assert.Null(t.HttpMethod); Assert.Null(t.HttpPath); });
}
```

- [ ] **Step 2: Run — expect FAIL (column missing)**

Run: `dotnet test tests/McpGateway.IntegrationTests --filter "FullyQualifiedName~DbContextTests"`
Expected: FAIL (schema).

- [ ] **Step 3: Update entity configuration**

In the `McpServerDefinition` EF config, add:
```csharp
    builder.Property(d => d.SourceType).HasConversion<string>().HasMaxLength(32).IsRequired().HasDefaultValue("openapi");
```
In the `ToolDefinition` config, ensure `HttpMethod`/`HttpPath` are now nullable columns:
```csharp
    builder.Property(t => t.HttpMethod).IsRequired(false);
    builder.Property(t => t.HttpPath).IsRequired(false);
```
And map `SourceType` read-back (`OnModelCreating` — match existing entity-config style used in the project).

- [ ] **Step 4: Write the SQL migration script (ADR-0004 — SQL-scripted, DBA-applied)**

`V8__add_source_type.sql` (adjust `V8` to the next migration number):
```sql
-- ADR-0007: add SourceType discriminator; relax tool HTTP coords for mcp-upstream tools
ALTER TABLE ai_gateway.mcp_server_defs
    ADD COLUMN IF NOT EXISTS source_type VARCHAR(32) NOT NULL DEFAULT 'openapi';

ALTER TABLE ai_gateway.tools
    ALTER COLUMN http_method DROP NOT NULL;

ALTER TABLE ai_gateway.tools
    ALTER COLUMN http_path DROP NOT NULL;
```

- [ ] **Step 5: Run — expect PASS (Testcontainers applies the migration script via the fixture)**

Run: `dotnet test tests/McpGateway.IntegrationTests --filter "FullyQualifiedName~DbContextTests"`
Expected: PASS. If the fixture doesn't auto-apply new SQL scripts, wire the new script into the test DB bootstrap (check `PostgreSqlFixture`).

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Persistence/ \
        tests/McpGateway.IntegrationTests/Persistence/DbContextTests.cs
git commit -m "feat(persistence): SourceType column + nullable tool HTTP coords (migration V8)"
```

---

## Task 11: End-to-end integration test against an in-proc upstream MCP server

**Files:**
- Create: `tests/McpGateway.IntegrationTests/McpUpstream/McpUpstreamIntegrationTests.cs`
- Create: `tests/McpGateway.IntegrationTests/McpUpstream/UpstreamMcpFixture.cs` (a real Kestrel-hosted MCP server on port 0)

- [ ] **Step 1: Write the failing end-to-end test**

```csharp
[Fact]
public async Task Register_list_and_call_an_upstream_mcp_server_end_to_end()
{
    await using var upstream = await UpstreamMcpFixture.StartAsync();   // real MCP server, port 0
    var upstreamUrl = upstream.Endpoint + "/mcp";

    // 1. register an mcp-upstream server pointing at the in-proc upstream
    var create = await _client.PostAsJsonAsync("/admin/servers", new
    {
        name = "echo-upstream",
        displayName = "Echo Upstream",
        sourceType = "mcp-upstream",
        upstreamUrl,
        authStrategy = "static",
        authConfig = new { }
    });
    create.EnsureSuccessStatusCode();

    // 2. approve it (so tools are loaded into the store)
    (await _client.PostAsync("/admin/servers/echo-upstream/approve", null)).EnsureSuccessStatusCode();

    // 3. tools/list through the gateway's /mcp/echo-upstream
    var list = await _client.PostAsync("/mcp/echo-upstream", Json("tools/list", null));
    var listBody = await list.Content.ReadAsStringAsync();
    Assert.Contains("echo", listBody);

    // 4. tools/call forwarded to the upstream
    var call = await _client.PostAsync("/mcp/echo-upstream",
        Json("tools/call", new { name = "echo", arguments = new { msg = "pluto" } }));
    var callBody = await call.Content.ReadAsStringAsync();
    Assert.Contains("pluto", callBody);
}
```
(`Json` builds a JSON-RPC 2.0 envelope helper; `_client` is the gateway `WebApplicationFactory` client.)

- [ ] **Step 2: Run — expect FAIL (upstream fixture / wiring incomplete)**

Run: `dotnet test tests/McpGateway.IntegrationTests --filter "FullyQualifiedName~McpUpstreamIntegrationTests"`
Expected: FAIL.

- [ ] **Step 3: Implement UpstreamMcpFixture — a minimal MCP server using the same SDK, on Kestrel port 0**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.AspNetCore;

public sealed class UpstreamMcpFixture : IAsyncDisposable
{
    private readonly WebApplication _app;
    public string Endpoint { get; }

    private UpstreamMcpFixture(WebApplication app, string endpoint) { _app = app; Endpoint = endpoint; }

    public static async Task<UpstreamMcpFixture> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddMcpServer().WithHttpTransport();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // ephemeral
        var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();
        var endpoint = app.Urls.First().Replace("127.0.0.1", "localhost"); // resolve actual port
        return new UpstreamMcpFixture(app, endpoint);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
```
Register one tool (`echo`) on the upstream using the SDK's attribute/tool pattern (e.g., a `[McpServerToolType]` class with an `Echo` method returning the `msg` argument). Confirm the exact tool-registration attribute via the SDK (preview surface).

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test tests/McpGateway.IntegrationTests --filter "FullyQualifiedName~McpUpstreamIntegrationTests"`
Expected: PASS. This is the walking-skeleton success criterion: gateway ingests a live upstream, serves its tools, and forwards a call.

- [ ] **Step 5: Commit**

```bash
git add tests/McpGateway.IntegrationTests/McpUpstream/
git commit -m "test: end-to-end mcp-upstream register/list/call against in-proc MCP server"
```

---

## Self-Review

**Spec coverage (ADR-0007 §1, §2, SDK client):**
- §1 `SourceType` discriminator — Tasks 2, 9, 10 ✅
- §2 re-host (catalog import, not transparent proxy) — Tasks 6, 7, 9 ✅
- SDK client (`ListToolsAsync` + `CallToolAsync` over StreamableHttp) — Tasks 1, 7 ✅
- Invocation polymorphism (branch-free `ToolCallHandler`) — Tasks 4, 5, 8 ✅
- Approval/audit/telemetry reused unchanged — Task 11 verifies the approval path; telemetry is carried through the strategy refactor in Task 5 ✅

**Deferred to Plan 2 (explicit):** auth-strategy refactor (`obo`/`credential` + OAuth-client-credentials + SP-assertion OBO + `passthrough`/`static` alias migration), refresh-diff for mcp-upstream, Propagated Principal / `Caller.PrincipalType`, sanitization of third-party tool descriptions. None are required for the walking skeleton.

**Placeholder scan:** Task 5's telemetry-status-tag adjustment is flagged as "adapt assertions" rather than a placeholder — it's a known refactor consequence with a concrete approach (carry status on `ToolCallResult`). Task 7's SDK-mapping note is a genuine preview-API verification, not a placeholder.

**Type consistency:** `UpstreamTool(Name, Description, InputSchema)` — used identically in Tasks 6, 7, 8, 9. `IToolInvocationStrategy.InvokeAsync(server, tool, args, ct)` — identical across Tasks 4, 5, 8. `IMcpUpstreamClient.ListToolsAsync/CallToolAsync` — identical in Tasks 7, 8, 9.

---

## Execution Handoff

Plan saved to `docs/superpowers/plans/2026-07-05-mcp-upstream-walking-skeleton.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
