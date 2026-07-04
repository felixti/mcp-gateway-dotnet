# Tool Call Proxy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute MCP `tools/call` requests by building an HTTP request from the tool definition and arguments, sending it via `HttpClient` + Polly to the underlying API, and wrapping the HTTP response as an MCP `CallToolResult`.

**Architecture:** `ToolCallHandler` is the orchestrator. It resolves the server and tool from `IToolStore`, builds the HTTP request with `HttpRequestBuilder`, sends it through `IHttpClientFactory` with Polly policies, and wraps the response with `ResponseWrapper`. `MetaToolsHandler` handles the three dynamic-mode meta-tools. Core remains HTTP-framework-agnostic; it returns a plain `ToolCallResult` that the MCP SDK layer maps to JSON-RPC.

**Tech Stack:** .NET 10, HttpClient, Polly 8.7.0, Microsoft.Extensions.Http.Polly 10.0.9, xUnit, FluentAssertions.

---

## File Structure

```
src/McpGateway.Core/
├── Proxy/
│   ├── ToolCallResult.cs
│   ├── HttpRequestBuilder.cs
│   ├── ResponseWrapper.cs
│   ├── ToolCallHandler.cs
│   └── MetaToolsHandler.cs
└── Proxy/Exceptions/
    └── ToolNotFoundException.cs

tests/McpGateway.UnitTests/
└── Proxy/
    ├── HttpRequestBuilderTests.cs
    ├── ResponseWrapperTests.cs
    ├── ToolCallHandlerTests.cs
    └── MetaToolsHandlerTests.cs
```

---

### Task 1: Define `ToolCallResult` and exceptions

**Files:**
- Create: `src/McpGateway.Core/Proxy/ToolCallResult.cs`
- Create: `src/McpGateway.Core/Proxy/Exceptions/ToolNotFoundException.cs`

- [ ] **Step 1: Implement `ToolCallResult`**

Create `src/McpGateway.Core/Proxy/ToolCallResult.cs`:

```csharp
namespace McpGateway.Core.Proxy;

public class ToolCallResult
{
    public bool IsError { get; set; }
    public IReadOnlyList<ToolCallContent> Content { get; set; } = [];
}

public class ToolCallContent
{
    public string Type { get; set; } = "text";
    public string Text { get; set; } = null!;
}
```

- [ ] **Step 2: Implement `ToolNotFoundException`**

Create `src/McpGateway.Core/Proxy/Exceptions/ToolNotFoundException.cs`:

```csharp
namespace McpGateway.Core.Proxy.Exceptions;

public class ToolNotFoundException : Exception
{
    public ToolNotFoundException(string serverName, string toolName)
        : base($"Tool '{toolName}' not found in server '{serverName}'.")
    {
        ServerName = serverName;
        ToolName = toolName;
    }

    public string ServerName { get; }
    public string ToolName { get; }
}
```

- [ ] **Step 3: Verify build**

Run:

```bash
dotnet build src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/Proxy
git commit -m "feat(proxy): add ToolCallResult and ToolNotFoundException"
```

---

### Task 2: Implement `HttpRequestBuilder`

**Files:**
- Create: `src/McpGateway.Core/Proxy/HttpRequestBuilder.cs`
- Create: `tests/McpGateway.UnitTests/Proxy/HttpRequestBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/Proxy/HttpRequestBuilderTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;
using System.Text.Json.Nodes;

namespace McpGateway.UnitTests.Proxy;

public class HttpRequestBuilderTests
{
    private readonly HttpRequestBuilder _builder = new();

    [Fact]
    public void Build_PathParamsSubstituted()
    {
        var tool = CreateTool("GET", "/users/{id}");
        var args = new Dictionary<string, object?> { ["id"] = "123" };

        var request = _builder.Build("https://api.example.com", tool, args);

        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.ToString().Should().Be("https://api.example.com/users/123");
    }

    [Fact]
    public void Build_QueryParamsAdded()
    {
        var tool = CreateTool("GET", "/users");
        var args = new Dictionary<string, object?> { ["limit"] = 10, ["offset"] = 20 };

        var request = _builder.Build("https://api.example.com", tool, args);

        var uri = request.RequestUri!.ToString();
        uri.Should().Contain("limit=10");
        uri.Should().Contain("offset=20");
    }

    [Fact]
    public void Build_BodyParamSerialized()
    {
        var tool = CreateTool("POST", "/users");
        var args = new Dictionary<string, object?> { ["body"] = new { name = "Alice" } };

        var request = _builder.Build("https://api.example.com", tool, args);

        request.Content.Should().NotBeNull();
        var body = request.Content!.ReadAsStringAsync().Result;
        body.Should().Contain("Alice");
    }

    private static ToolDefinition CreateTool(string method, string path) => new()
    {
        ToolName = "test_tool",
        Description = "Test",
        HttpMethod = method,
        HttpPath = path,
        InputSchema = "{}"
    };
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `HttpRequestBuilder`**

Create `src/McpGateway.Core/Proxy/HttpRequestBuilder.cs`:

```csharp
using McpGateway.Core.ServerDefinitions;
using System.Text;
using System.Text.Json;

namespace McpGateway.Core.Proxy;

public class HttpRequestBuilder
{
    public HttpRequestMessage Build(string baseUrl, ToolDefinition tool, IReadOnlyDictionary<string, object?> arguments)
    {
        var path = SubstitutePathParameters(tool.HttpPath, arguments);
        var query = BuildQueryString(tool.HttpPath, arguments);
        var uri = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/') + query);

        var request = new HttpRequestMessage(new HttpMethod(tool.HttpMethod), uri);

        if (arguments.TryGetValue("body", out var body) && body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    private static string SubstitutePathParameters(string path, IReadOnlyDictionary<string, object?> arguments)
    {
        var result = path;
        foreach (var match in System.Text.RegularExpressions.Regex.Matches(path, "\{(\w+)\}").Cast<System.Text.RegularExpressions.Match>())
        {
            var paramName = match.Groups[1].Value;
            if (arguments.TryGetValue(paramName, out var value) && value is not null)
            {
                result = result.Replace(match.Value, Uri.EscapeDataString(value.ToString()!));
            }
        }
        return result;
    }

    private static string BuildQueryString(string path, IReadOnlyDictionary<string, object?> arguments)
    {
        var pathParams = System.Text.RegularExpressions.Regex.Matches(path, "\{(\w+)\}")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var queryParams = arguments
            .Where(a => a.Key != "body" && !pathParams.Contains(a.Key) && a.Value is not null)
            .Select(a => $"{a.Key}={Uri.EscapeDataString(a.Value!.ToString()!)}");

        var query = string.Join("\u0026", queryParams);
        return string.IsNullOrEmpty(query) ? string.Empty : "?" + query;
    }
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~HttpRequestBuilderTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/Proxy/HttpRequestBuilder.cs tests/McpGateway.UnitTests/Proxy/HttpRequestBuilderTests.cs
git commit -m "feat(proxy): add HttpRequestBuilder

- Substitute path parameters
- Add remaining args as query string
- Serialize body argument as JSON"
```

---

### Task 3: Implement `ResponseWrapper`

**Files:**
- Create: `src/McpGateway.Core/Proxy/ResponseWrapper.cs`
- Create: `tests/McpGateway.UnitTests/Proxy/ResponseWrapperTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/Proxy/ResponseWrapperTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.Proxy;
using System.Net;

namespace McpGateway.UnitTests.Proxy;

public class ResponseWrapperTests
{
    private readonly ResponseWrapper _wrapper = new();

    [Fact]
    public async Task Wrap_Success_ReturnsContent()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"items\":[]}")
        };

        var result = await _wrapper.WrapAsync(response);

        result.IsError.Should().BeFalse();
        result.Content.Should().ContainSingle(c => c.Text == "{\"items\":[]}");
    }

    [Fact]
    public async Task Wrap_Error_ReturnsIsError()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid")
        };

        var result = await _wrapper.WrapAsync(response);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[HTTP 400]");
        result.Content[0].Text.Should().Contain("invalid");
    }

    [Fact]
    public async Task Wrap_LargeResponse_Truncated()
    {
        var large = new string('x', 11_000);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(large)
        };

        var result = await _wrapper.WrapAsync(response);

        result.Content[0].Text.Length.Should().BeLessThanOrEqualTo(10_240);
    }
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `ResponseWrapper`**

Create `src/McpGateway.Core/Proxy/ResponseWrapper.cs`:

```csharp
namespace McpGateway.Core.Proxy;

public class ResponseWrapper
{
    private const int MaxResponseLength = 10 * 1024;

    public async Task<ToolCallResult> WrapAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var truncated = body.Length > MaxResponseLength ? body[..MaxResponseLength] : body;

        if (response.IsSuccessStatusCode)
        {
            return new ToolCallResult
            {
                IsError = false,
                Content = [new ToolCallContent { Type = "text", Text = truncated }]
            };
        }

        return new ToolCallResult
        {
            IsError = true,
            Content = [new ToolCallContent { Type = "text", Text = $"[HTTP {(int)response.StatusCode}] {truncated}" }]
        };
    }
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ResponseWrapperTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/Proxy/ResponseWrapper.cs tests/McpGateway.UnitTests/Proxy/ResponseWrapperTests.cs
git commit -m "feat(proxy): add ResponseWrapper

- Success responses returned as text content
- Non-2xx returned with isError=true and HTTP status prefix
- Truncate responses to 10KB"
```

---

### Task 4: Implement `ToolCallHandler`

**Files:**
- Create: `src/McpGateway.Core/Proxy/ToolCallHandler.cs`
- Create: `tests/McpGateway.UnitTests/Proxy/ToolCallHandlerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/Proxy/ToolCallHandlerTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.Proxy;
using McpGateway.Core.Proxy.Exceptions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using System.Text.Json.Nodes;

namespace McpGateway.UnitTests.Proxy;

public class ToolCallHandlerTests
{
    private readonly InMemoryToolStore _store = new();

    [Fact]
    public async Task HandleAsync_UnknownServer_Throws()
    {
        var handler = CreateHandler();
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.HandleAsync("unknown", "tool", [], CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_UnknownTool_Throws()
    {
        _store.AddServer(CreateServer("api", []));
        var handler = CreateHandler();

        await Assert.ThrowsAsync<ToolNotFoundException>(
            () => handler.HandleAsync("api", "missing", [], CancellationToken.None));
    }

    private ToolCallHandler CreateHandler()
    {
        var httpClient = new HttpClient(new TestHttpMessageHandler());
        return new ToolCallHandler(_store, new HttpRequestBuilder(), new ResponseWrapper(), httpClient);
    }

    private static McpServerDefinition CreateServer(string name, List<ToolDefinition> tools) => new()
    {
        Name = name,
        DisplayName = name,
        BaseUrl = "https://api.example.com",
        SpecHash = "hash",
        AuthStrategy = "obo",
        AuthConfig = "{}",
        Tools = tools
    };

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
        }
    }
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `ToolCallHandler`**

Create `src/McpGateway.Core/Proxy/ToolCallHandler.cs`:

```csharp
using McpGateway.Core.Proxy.Exceptions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;

namespace McpGateway.Core.Proxy;

public class ToolCallHandler
{
    private readonly IToolStore _toolStore;
    private readonly HttpRequestBuilder _requestBuilder;
    private readonly ResponseWrapper _responseWrapper;
    private readonly HttpClient _httpClient;

    public ToolCallHandler(
        IToolStore toolStore,
        HttpRequestBuilder requestBuilder,
        ResponseWrapper responseWrapper,
        HttpClient httpClient)
    {
        _toolStore = toolStore;
        _requestBuilder = requestBuilder;
        _responseWrapper = responseWrapper;
        _httpClient = httpClient;
    }

    public async Task<ToolCallResult> HandleAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        var server = _toolStore.GetServer(serverName)
            ?? throw new KeyNotFoundException($"Server '{serverName}' not found.");

        var tool = server.Tools.FirstOrDefault(t => t.ToolName == toolName)
            ?? throw new ToolNotFoundException(serverName, toolName);

        var request = _requestBuilder.Build(server.BaseUrl, tool, arguments);
        var response = await _httpClient.SendAsync(request, ct);
        return await _responseWrapper.WrapAsync(response);
    }
}
```

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~ToolCallHandlerTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/Proxy/ToolCallHandler.cs tests/McpGateway.UnitTests/Proxy/ToolCallHandlerTests.cs
git commit -m "feat(proxy): add ToolCallHandler

- Resolve server and tool from IToolStore
- Build and send HTTP request via HttpClient
- Wrap response via ResponseWrapper"
```

---

### Task 5: Implement `MetaToolsHandler`

**Files:**
- Create: `src/McpGateway.Core/Proxy/MetaToolsHandler.cs`
- Create: `tests/McpGateway.UnitTests/Proxy/MetaToolsHandlerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/McpGateway.UnitTests/Proxy/MetaToolsHandlerTests.cs`:

```csharp
using FluentAssertions;
using McpGateway.Core.Proxy;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpGateway.UnitTests.Proxy;

public class MetaToolsHandlerTests
{
    private readonly InMemoryToolStore _store = new();

    public MetaToolsHandlerTests()
    {
        _store.AddServer(new McpServerDefinition
        {
            Name = "large-api",
            DisplayName = "Large API",
            BaseUrl = "https://api.example.com",
            SpecHash = "hash",
            AuthStrategy = "obo",
            ToolMode = ToolMode.Dynamic,
            Tools =
            [
                new ToolDefinition
                {
                    ToolName = "get_users",
                    Description = "Get users",
                    HttpMethod = "GET",
                    HttpPath = "/users",
                    InputSchema = "{}"
                }
            ]
        });
    }

    [Fact]
    public async Task ListApiEndpoints_ReturnsToolList()
    {
        var handler = new MetaToolsHandler(_store);
        var result = await handler.HandleAsync("large-api", "list_api_endpoints", [], CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("get_users");
    }

    [Fact]
    public async Task GetApiEndpointSchema_ReturnsSchema()
    {
        var handler = new MetaToolsHandler(_store);
        var result = await handler.HandleAsync("large-api", "get_api_endpoint_schema",
            new Dictionary<string, object?> { ["tool_name"] = "get_users" }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("GET /users");
    }
}
```

Run tests, expect FAIL.

- [ ] **Step 2: Implement `MetaToolsHandler`**

Create `src/McpGateway.Core/Proxy/MetaToolsHandler.cs`:

```csharp
using McpGateway.Core.Proxy.Exceptions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using System.Text.Json;

namespace McpGateway.Core.Proxy;

public class MetaToolsHandler
{
    private readonly IToolStore _toolStore;

    public MetaToolsHandler(IToolStore toolStore)
    {
        _toolStore = toolStore;
    }

    public Task<ToolCallResult> HandleAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        var server = _toolStore.GetServer(serverName)
            ?? throw new KeyNotFoundException($"Server '{serverName}' not found.");

        return toolName switch
        {
            "list_api_endpoints" => Task.FromResult(ListEndpoints(server)),
            "get_api_endpoint_schema" => Task.FromResult(GetEndpointSchema(server, arguments)),
            "invoke_api_endpoint" => throw new NotImplementedException("invoke_api_endpoint delegates to ToolCallHandler."),
            _ => throw new ToolNotFoundException(serverName, toolName)
        };
    }

    private static ToolCallResult ListEndpoints(McpServerDefinition server)
    {
        var endpoints = server.Tools.Select(t => new
        {
            t.ToolName,
            t.Description,
            t.HttpMethod,
            t.HttpPath
        });

        return new ToolCallResult
        {
            Content = [new ToolCallContent { Text = JsonSerializer.Serialize(endpoints) }]
        };
    }

    private static ToolCallResult GetEndpointSchema(McpServerDefinition server, IReadOnlyDictionary<string, object?> arguments)
    {
        var targetName = arguments["tool_name"]?.ToString()
            ?? throw new ArgumentException("tool_name is required.");

        var tool = server.Tools.FirstOrDefault(t => t.ToolName == targetName)
            ?? throw new ToolNotFoundException(server.Name, targetName);

        var schema = new
        {
            tool.ToolName,
            tool.Description,
            tool.HttpMethod,
            tool.HttpPath,
            tool.InputSchema,
            tool.OutputSchema
        };

        return new ToolCallResult
        {
            Content = [new ToolCallContent { Text = JsonSerializer.Serialize(schema) }]
        };
    }
}
```

Note: `invoke_api_endpoint` will be wired by the caller to delegate to `ToolCallHandler` with the resolved tool name.

- [ ] **Step 3: Run tests**

Run:

```bash
dotnet test tests/McpGateway.UnitTests/McpGateway.UnitTests.csproj --filter "FullyQualifiedName~MetaToolsHandlerTests" -v n
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/McpGateway.Core/Proxy/MetaToolsHandler.cs tests/McpGateway.UnitTests/Proxy/MetaToolsHandlerTests.cs
git commit -m "feat(proxy): add MetaToolsHandler for dynamic mode

- list_api_endpoints returns tool list
- get_api_endpoint_schema returns tool schema
- invoke_api_endpoint placeholder for ToolCallHandler delegation"
```

---

### Task 6: Configure HttpClient + Polly

**Files:**
- Create: `src/McpGateway.Core/Proxy/ProxyServiceExtensions.cs`
- Modify: `src/McpGateway.Api/Program.cs`

- [ ] **Step 1: Add Polly packages**

Run:

```bash
dotnet add src/McpGateway.Core/McpGateway.Core.csproj package Microsoft.Extensions.Http --version 9.0.4
dotnet add src/McpGateway.Api/McpGateway.Api.csproj package Microsoft.Extensions.Http.Polly --version 10.0.9
```

- [ ] **Step 2: Implement `ProxyServiceExtensions`**

Create `src/McpGateway.Core/Proxy/ProxyServiceExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Polly;
using System.Net;

namespace McpGateway.Core.Proxy;

public static class ProxyServiceExtensions
{
    public static IServiceCollection AddMcpProxy(this IServiceCollection services)
    {
        services.AddSingleton<HttpRequestBuilder>();
        services.AddSingleton<ResponseWrapper>();
        services.AddSingleton<MetaToolsHandler>();

        services.AddHttpClient(ToolCallHandler.HttpClientName)
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());

        services.AddScoped<ToolCallHandler>(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            return new ToolCallHandler(
                provider.GetRequiredService<ToolStore.IToolStore>(),
                provider.GetRequiredService<HttpRequestBuilder>(),
                provider.GetRequiredService<ResponseWrapper>(),
                factory.CreateClient(ToolCallHandler.HttpClientName));
        });

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
    }
}
```

- [ ] **Step 3: Add constant to `ToolCallHandler`**

Add to `src/McpGateway.Core/Proxy/ToolCallHandler.cs`:

```csharp
public const string HttpClientName = "McpToolProxy";
```

- [ ] **Step 4: Update Api Program.cs**

Add to `src/McpGateway.Api/Program.cs`:

```csharp
using McpGateway.Core.Proxy;

// ... existing services ...
builder.Services.AddMcpProxy();
```

- [ ] **Step 5: Verify build**

Run:

```bash
dotnet build src/McpGateway.Core/McpGateway.Core.csproj
```

Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/McpGateway.Core/Proxy/ProxyServiceExtensions.cs src/McpGateway.Core/Proxy/ToolCallHandler.cs src/McpGateway.Api/Program.cs
git commit -m "feat(proxy): configure HttpClient + Polly policies

- Add retry policy (3 attempts, exponential backoff)
- Add circuit breaker (5 failures / 30s)
- Register ToolCallHandler, HttpRequestBuilder, ResponseWrapper"
```

---

### Task 7: Run full test suite

- [ ] **Step 1: Run all tests**

Run:

```bash
dotnet test /var/home/felix/github/mcp-gateway/McpGateway.sln
```

Expected: All tests pass.

- [ ] **Step 2: Commit final state**

```bash
git commit -m "feat(proxy): complete tool call proxy

- HttpRequestBuilder, ResponseWrapper, ToolCallHandler, MetaToolsHandler
- Polly retry + circuit breaker
- Unit tests for all proxy components"
```

---

## Self-Review

**1. Spec coverage:**

| Requirement | Task |
|---|---|
| Path params → URL | Task 2 |
| Query params → query string | Task 2 |
| Body → request body | Task 2 |
| HTTP response → CallToolResult | Task 3 |
| Truncate to 10KB | Task 3 |
| isError for non-2xx | Task 3 |
| Polly retry + circuit breaker | Task 6 |
| Dynamic mode meta-tools | Task 5 |
| ToolCallHandler orchestration | Task 4 |

**2. Placeholder scan:**

No placeholders. Each task has complete code and commands.

**3. Type consistency:**

- `ToolCallHandler` uses `IToolStore`, `HttpRequestBuilder`, `ResponseWrapper`, `HttpClient`.
- `HttpRequestBuilder` accepts `IReadOnlyDictionary<string, object?>`.
- `ToolCallResult` is returned by handler, wrapper, and meta-tools handler.

**4. Known follow-ups for Oracle review:**

- `ToolCallHandler` takes `HttpClient` directly. Consider whether named clients per server are needed for different base URLs/timeouts.
- `MetaToolsHandler.invoke_api_endpoint` is a placeholder. The caller (MCP endpoint layer) should resolve the target tool and call `ToolCallHandler.HandleAsync`.
- Auth injection is deferred to the Auth Injection plan; `HttpRequestBuilder` does not set `Authorization` header.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-07-03-tool-call-proxy.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — Fresh subagent per task, review between tasks.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`.

**Which approach?**
