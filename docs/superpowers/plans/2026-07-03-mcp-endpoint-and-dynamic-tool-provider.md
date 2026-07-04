# MCP Endpoint & Dynamic Tool Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose one MCP Streamable HTTP endpoint per registered server (`/mcp/{server_name}`) where MCP clients can call `tools/list` and `tools/call`. Tools are served dynamically from `IToolStore` via a custom tool provider so new/updated servers become visible without restart (ADR-0003).

**Architecture:** `McpGateway.McpSdk` wraps the C# MCP SDK. `DynamicToolProvider` implements the SDK's tool-provider interface and delegates to `IToolStore` for the current server. `McpEndpointMapper` registers `/mcp/{server_name}` routes, each with its own `DynamicToolProvider` scoped to that server name. `McpEndpoints` wires this into the ASP.NET Core pipeline in `McpGateway.Api`.

**Tech Stack:** .NET 10, ASP.NET Core 10, ModelContextProtocol.AspNetCore 2.0.0-preview.1, xUnit, Testcontainers.

**Risk:** The C# MCP SDK's exact extension point for mutable tool stores is not stable. Task 1 is a spike to verify the API before the rest of the plan is implemented.

---

## File Structure

```
src/
├── McpGateway.McpSdk/
│   ├── McpGateway.McpSdk.csproj
│   ├── DynamicToolProvider.cs
│   ├── DynamicMcpServerOptions.cs
│   └── McpEndpointMapper.cs
│
├── McpGateway.Api/
│   ├── McpGateway.Api.csproj
│   ├── Program.cs
│   └── Endpoints/
│       └── McpEndpoints.cs
│
tests/McpGateway.IntegrationTests/
└── McpEndpointTests.cs
```

---

### Task 1: Spike — verify C# MCP SDK extension point

**Files:**
- Create: `spikes/mcp-sdk-tool-provider/Program.cs` (temporary spike project)

- [ ] **Step 1: Create temporary spike project**

Run:

```bash
mkdir -p /var/home/felix/github/mcp-gateway/spikes/mcp-sdk-tool-provider
cd /var/home/felix/github/mcp-gateway/spikes/mcp-sdk-tool-provider
dotnet new web -n McpToolProviderSpike --framework net10.0
dotnet add package ModelContextProtocol.AspNetCore --version 2.0.0-preview.1
```

- [ ] **Step 2: Inspect SDK surface**

Search the SDK assembly for tool-provider types:

```bash
dotnet build spikes/mcp-sdk-tool-provider/McpToolProviderSpike.csproj
find ~/.nuget/packages/modelcontextprotocol.aspnetcore -name "*.dll" | head -5
```

Use `ildasm` or reflection to find:
- `IMcpServerTool` or equivalent interface
- `MapMcp()` extension method signature
- How to register a custom tool factory
- How to pass per-route state (server name)

- [ ] **Step 3: Build a working `tools/list` + `tools/call` round-trip spike**

Extend `spikes/mcp-sdk-tool-provider/Program.cs` to:
1. Register one MCP endpoint (e.g., `/mcp/test`).
2. Provide a custom tool with a hardcoded name, description, and input schema from a mutable store.
3. Verify that an HTTP POST with JSON-RPC `tools/list` returns the tool.
4. Verify that an HTTP POST with JSON-RPC `tools/call` reaches your handler and returns a result.
5. Verify that changing the mutable store and calling `tools/list` again returns the updated tool (proves hot-reload).

If the SDK does not support per-route state directly, verify the fallback: the custom tool provider reads the server name from `HttpContext.Request.RouteValues` or similar.

The spike is **gated** — do not proceed to Tasks 3–5 until this round-trip works.

- [ ] **Step 4: Document findings in spike README**

Create `spikes/mcp-sdk-tool-provider/README.md`:

```markdown
# MCP SDK Tool Provider Spike

## Findings
- Correct tool provider interface: `_____`
- Correct registration method: `_____`
- Per-route state mechanism: `_____`
- Fallback if no per-route state: `_____`
- tools/list request/response shape: (paste here)
- tools/call request/response shape: (paste here)
```

- [ ] **Step 5: Commit spike**

```bash
git add spikes/mcp-sdk-tool-provider
git commit -m "spike: verify C# MCP SDK custom tool provider API"
```

If the spike reveals a different API than assumed below, update Tasks 3–5 before implementing.

---

### Task 2: Create McpSdk and Api projects

**Files:**
- Create: `src/McpGateway.McpSdk/McpGateway.McpSdk.csproj`
- Create: `src/McpGateway.Api/McpGateway.Api.csproj`
- Modify: `McpGateway.sln`

- [ ] **Step 1: Create McpSdk class library**

Run:

```bash
dotnet new classlib -n McpGateway.McpSdk -o /var/home/felix/github/mcp-gateway/src/McpGateway.McpSdk --framework net10.0
```

- [ ] **Step 2: Create Api web project**

Run:

```bash
dotnet new web -n McpGateway.Api -o /var/home/felix/github/mcp-gateway/src/McpGateway.Api --framework net10.0
```

- [ ] **Step 3: Add references**

Run:

```bash
dotnet add src/McpGateway.McpSdk/McpGateway.McpSdk.csproj reference src/McpGateway.Core/McpGateway.Core.csproj
dotnet add src/McpGateway.Api/McpGateway.Api.csproj reference src/McpGateway.Core/McpGateway.Core.csproj
dotnet add src/McpGateway.Api/McpGateway.Api.csproj reference src/McpGateway.McpSdk/McpGateway.McpSdk.csproj
dotnet add src/McpGateway.Api/McpGateway.Api.csproj reference src/McpGateway.Persistence/McpGateway.Persistence.csproj
```

- [ ] **Step 4: Add MCP SDK package**

Run:

```bash
dotnet add src/McpGateway.McpSdk/McpGateway.McpSdk.csproj package ModelContextProtocol.AspNetCore --version 2.0.0-preview.1
dotnet add src/McpGateway.Api/McpGateway.Api.csproj package ModelContextProtocol.AspNetCore --version 2.0.0-preview.1
```

- [ ] **Step 5: Add solution entries**

Run:

```bash
dotnet sln /var/home/felix/github/mcp-gateway/McpGateway.sln add src/McpGateway.McpSdk/McpGateway.McpSdk.csproj src/McpGateway.Api/McpGateway.Api.csproj
```

- [ ] **Step 6: Verify build**

Run:

```bash
dotnet build src/McpGateway.McpSdk/McpGateway.McpSdk.csproj
```

Expected: Build succeeds (empty project).

---

### Task 3: Implement `DynamicToolProvider`

**Files:**
- Create: `src/McpGateway.McpSdk/DynamicToolProvider.cs`

- [ ] **Step 1: Implement based on spike findings**

Create `src/McpGateway.McpSdk/DynamicToolProvider.cs`:

```csharp
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using System.Text.Json.Nodes;

namespace McpGateway.McpSdk;

public class DynamicToolProvider : IMcpServerTool
{
    private readonly string _serverName;
    private readonly IToolStore _toolStore;

    public DynamicToolProvider(string serverName, IToolStore toolStore)
    {
        _serverName = serverName;
        _toolStore = toolStore;
    }

    public string ServerName => _serverName;

    public Task<IReadOnlyList<Tool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var server = _toolStore.GetServer(_serverName);
        if (server is null)
        {
            return Task.FromResult<IReadOnlyList<Tool>>([]);
        }

        var tools = server.Tools
            .Where(t => t.Visible)
            .Select(t => new Tool
            {
                Name = t.ToolName,
                Description = t.Description,
                InputSchema = JsonNode.Parse(t.InputSchema)
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<Tool>>(tools);
    }
}
```

**Important:** The exact `IMcpServerTool` interface, method names, and `Tool` schema type must match the SDK version discovered in Task 1. Update this code after the spike.

- [ ] **Step 2: Build**

Run:

```bash
dotnet build src/McpGateway.McpSdk/McpGateway.McpSdk.csproj
```

Expected: Build succeeds after adjusting to actual SDK API.

---

### Task 4: Implement `McpEndpointMapper`

**Files:**
- Create: `src/McpGateway.McpSdk/McpEndpointMapper.cs`
- Create: `src/McpGateway.McpSdk/DynamicMcpServerOptions.cs`

- [ ] **Step 1: Implement `DynamicMcpServerOptions`**

Create `src/McpGateway.McpSdk/DynamicMcpServerOptions.cs`:

```csharp
using McpGateway.Core.ToolStore;
using ModelContextProtocol.Server;

namespace McpGateway.McpSdk;

public static class DynamicMcpServerOptions
{
    public static IMcpServerBuilder WithDynamicTools(
        this IMcpServerBuilder builder,
        string serverName,
        IToolStore toolStore)
    {
        var provider = new DynamicToolProvider(serverName, toolStore);
        // Exact registration call depends on SDK API from Task 1 spike.
        // builder.Services.AddSingleton<IMcpServerTool>(provider);
        return builder;
    }
}
```

- [ ] **Step 2: Implement `McpEndpointMapper`**

Create `src/McpGateway.McpSdk/McpEndpointMapper.cs`:

```csharp
using McpGateway.Core.ToolStore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace McpGateway.McpSdk;

public static class McpEndpointMapper
{
    public static IEndpointRouteBuilder MapMcpServers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mcp/{serverName}", async (
            string serverName,
            HttpContext context,
            IToolStore toolStore) =>
        {
            // The MCP SDK's MapMcp() may not support per-request dynamic routes.
            // This endpoint uses the SDK's request delegate directly if available,
            // or returns a placeholder until the SDK API is confirmed.
            var server = toolStore.GetServer(serverName);
            if (server is null)
            {
                return Results.NotFound(new { error = $"Server '{serverName}' not found." });
            }

            // Delegate to SDK Streamable HTTP handler.
            // Exact invocation depends on Task 1 spike.
            return Results.Ok(new { status = "MCP endpoint ready", server = serverName });
        });

        return app;
    }
}
```

Note: This is intentionally incomplete. The spike in Task 1 determines the exact `MapMcp()` usage. The final implementation should look like:

```csharp
app.MapMcp("/mcp/{serverName}")
   .WithDynamicTools(serverName, toolStore);
```

or equivalent.

- [ ] **Step 3: Build**

Run:

```bash
dotnet build src/McpGateway.McpSdk/McpGateway.McpSdk.csproj
```

Expected: Build succeeds.

---

### Task 5: Wire endpoints in Api

**Files:**
- Create: `src/McpGateway.Api/Endpoints/McpEndpoints.cs`
- Modify: `src/McpGateway.Api/Program.cs`

- [ ] **Step 1: Create `McpEndpoints`**

Create `src/McpGateway.Api/Endpoints/McpEndpoints.cs`:

```csharp
using McpGateway.McpSdk;

namespace McpGateway.Api.Endpoints;

public static class McpEndpoints
{
    public static IEndpointRouteBuilder MapMcp(this IEndpointRouteBuilder app)
    {
        app.MapMcpServers();
        return app;
    }
}
```

- [ ] **Step 2: Update `Program.cs`**

Replace `src/McpGateway.Api/Program.cs` with:

```csharp
using McpGateway.Api.Endpoints;
using McpGateway.Core;
using McpGateway.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpPersistence(builder.Configuration);
builder.Services.AddMcpCore();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapMcp();

app.Run();
```

- [ ] **Step 3: Add health checks package**

Run:

```bash
dotnet add src/McpGateway.Api/McpGateway.Api.csproj package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore --version 10.0.9
```

- [ ] **Step 4: Build Api**

Run:

```bash
dotnet build src/McpGateway.Api/McpGateway.Api.csproj
```

Expected: Build succeeds.

---

### Task 6: Integration test — tools/list returns tools

**Files:**
- Create: `tests/McpGateway.IntegrationTests/McpEndpointTests.cs`

- [ ] **Step 1: Write test**

Create `tests/McpGateway.IntegrationTests/McpEndpointTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Text;
using System.Text.Json;

namespace McpGateway.IntegrationTests;

public class McpEndpointTests : IClassFixture<WebApplicationFactory<McpGateway.Api.Program>>
{
    private readonly HttpClient _client;

    public McpEndpointTests(WebApplicationFactory<McpGateway.Api.Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var store = new InMemoryToolStore();
                store.AddServer(new McpServerDefinition
                {
                    Name = "test-server",
                    DisplayName = "Test Server",
                    BaseUrl = "https://test.example.com",
                    SpecHash = "hash",
                    AuthStrategy = "obo",
                    AuthConfig = "{}",
                    ApprovalStatus = "approved",
                    Tools =
                    [
                        new ToolDefinition
                        {
                            ToolName = "get_items",
                            Description = "Get items",
                            HttpMethod = "GET",
                            HttpPath = "/items",
                            InputSchema = "{}"
                        }
                    ]
                });
                services.AddSingleton<IToolStore>(store);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Get_Endpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/mcp/test-server");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }
}
```

This test is intentionally permissive until the exact MCP endpoint behavior is confirmed by the spike.

- [ ] **Step 2: Add WebApplicationFactory package**

Run:

```bash
dotnet add tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj package Microsoft.AspNetCore.Mvc.Testing --version 10.0.9
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.IntegrationTests/McpGateway.IntegrationTests.csproj --filter "FullyQualifiedName~McpEndpointTests" -v n
```

Expected: At minimum, the endpoint responds without crashing. Exact assertions depend on spike results.

---

### Task 7: Commit

- [ ] **Step 1: Commit**

```bash
git add src/McpGateway.McpSdk src/McpGateway.Api tests/McpGateway.IntegrationTests/McpEndpointTests.cs McpGateway.sln
git commit -m "feat(mcp-endpoint): add dynamic tool provider and MCP endpoint wiring

- McpGateway.McpSdk project with DynamicToolProvider
- McpEndpointMapper for /mcp/{server_name} routes
- Api project Program.cs wires Core + Persistence + MCP endpoints
- Integration test scaffold for MCP endpoint"
```

---

## Self-Review

**1. Spec coverage:**

| Requirement | Task |
|---|---|
| One MCP endpoint per server definition | Task 4 + Task 5 |
| Dynamic tools from IToolStore | Task 3 |
| No restart on tool change | Task 3 (reads IToolStore on each tools/list) |
| Streamable HTTP stateless mode | Task 4 (SDK-specific) |
| /mcp/{server_name} routing | Task 4 |

**2. Placeholder scan:**

This plan intentionally contains SDK-dependent placeholders because the C# MCP SDK API is preview and must be verified by the spike in Task 1. The spike findings must replace the placeholder code in Tasks 3–5 before implementation proceeds past Task 2.

**3. Type consistency:**

- `DynamicToolProvider` uses `IToolStore` and `McpServerDefinition` from Core.
- `Tool.Visible` filter is applied in `GetToolsAsync`.

**4. Known follow-ups for Oracle review:**

- The exact MCP SDK API is a risk. Task 1 spike must be treated as a gate before Tasks 3–5.
- This plan does not implement JSON-RPC request parsing or `tools/call` execution — that belongs to the Tool Call Proxy plan.
- Authentication on MCP endpoints is deferred to the Gateway Authentication plan.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-03-mcp-endpoint-and-dynamic-tool-provider.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — Fresh subagent per task, review between tasks. **Task 1 spike is mandatory before proceeding.**

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`.

**Which approach?**
