# MCP SDK Tool Provider Spike

Verifies the real `ModelContextProtocol.AspNetCore` 2.0.0-preview.1 API for
serving tools dynamically from a mutable in-memory store at a per-server
endpoint (`/mcp/{serverName}`).

## TL;DR

- The plan's `IMcpServerTool` interface does not exist.
- The real extension points are `IMcpServerBuilder.WithListToolsHandler(...)`
  and `IMcpServerBuilder.WithCallToolHandler(...)`.
- The endpoint is registered with `MapMcp("/mcp/{serverName}")`.
- Per-route state flows through `HttpContext.Items`, set inside
  `HttpServerTransportOptions.ConfigureSessionOptions` and read from
  `IHttpContextAccessor` inside the handlers (resolved from
  `RequestContext.Services`).
- Stateless mode (`opts.Stateless = true`) is required so the SDK
  instantiates a fresh `McpServer` per request and re-invokes
  `ConfigureSessionOptions` for each — this is what makes the route value
  available.

## What the spike proves

| # | Behaviour | Result |
|---|-----------|--------|
| 1 | POST `/mcp/approved` with `tools/list` | returns the seeded `greet` tool with the correct JSON schema |
| 2 | POST `/mcp/approved` with `tools/call {name: greet, arguments: {name: World}}` | handler invoked, returns `greet ok: {"name":"World"}` |
| 3 | POST `/mcp/pending` with `tools/list` | returns `tools: []` (unapproved path) |
| 4 | POST `/mcp/approved` with `tools/call {name: nope}` | returns `isError: true` with `tool 'nope' not found` |
| 5 | `POST /admin/tools/listinvoices` (mutates the store) followed by `tools/list` | the new tool appears without a process restart |
| 6 | `initialize` handshake | SDK responds with `serverInfo`, `protocolVersion: 2025-06-18`, `capabilities.tools: {}` |
| 7 | `ping` | returns `{}` |

## Actual SDK API

### Registration

```csharp
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "...", Version = "..." };
    })
    .WithHttpTransport(opts =>
    {
        opts.Stateless = true;                                  // required: per-request server
        opts.ConfigureSessionOptions = (ctx, serverOptions, ct) =>
        {
            // pull route value, stash on HttpContext.Items
            if (ctx.Request.RouteValues.TryGetValue("serverName", out var raw) && raw is string s)
                ctx.Items["mcp.server.name"] = s;
            return Task.CompletedTask;
        };
    })
    .WithListToolsHandler((request, ct) =>                     // ValueTask<ListToolsResult>
    {
        var serverName = ResolveServerName(request.Services);
        // ... build tools for this server
        return ValueTask.FromResult(new ListToolsResult { Tools = tools });
    })
    .WithCallToolHandler((request, ct) =>                      // ValueTask<CallToolResult>
    {
        var serverName = ResolveServerName(request.Services);
        // ... dispatch to ToolCallHandler
        return ValueTask.FromResult(new CallToolResult { Content = [...] });
    });
```

### Endpoint mapping

```csharp
app.MapMcp("/mcp/{serverName}");
```

`MapMcp(IEndpointRouteBuilder, string pattern)` lives in
`ModelContextProtocol.AspNetCore.McpEndpointRouteBuilderExtensions`.

### Per-route state

- **Pattern:** `HttpServerTransportOptions.ConfigureSessionOptions` is called
  for every request in stateless mode, with the live `HttpContext`. The
  callback stashes the route value (server name) on `HttpContext.Items`.
- **Read in handler:** `request.Services` is the DI scope for the request.
  `services.GetService<IHttpContextAccessor>().HttpContext.Items["mcp.server.name"]`
  yields the server name.
- **`IHttpContextAccessor` must be registered** via
  `builder.Services.AddHttpContextAccessor()` — required for the spike to work
  in stateless mode. (Without it, the property will be null.)
- **Why not `McpServerOptions.Snippets`?** — that property does not exist in
  this preview. `McpServerOptions` is configured fresh per request from
  `ConfigureSessionOptions`, but it has no user-data slot. `HttpContext.Items`
  is the only place that survives from configure to handler.

### Why stateless mode

In stateful mode the SDK creates a long-lived session per initialize. The
`serverName` route value would only be available on the very first request;
subsequent `tools/list` calls inside the same session would have no `route
values` because the transport reuses the session. **Stateless mode is required
for dynamic per-request routing.**

## Request / response shapes

### `tools/list` request

```http
POST /mcp/approved
Content-Type: application/json
Accept: application/json, text/event-stream

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list",
  "params": {}
}
```

### `tools/list` response

```http
HTTP/1.1 200 OK
Content-Type: text/event-stream

event: message
data: {
  "result": {
    "tools": [
      {
        "name": "greet",
        "description": "Returns a greeting for the given name.",
        "inputSchema": {
          "type": "object",
          "properties": { "name": { "type": "string" } },
          "required": ["name"]
        }
      }
    ],
    "ttlMs": 0,
    "cacheScope": "private"
  },
  "id": 1,
  "jsonrpc": "2.0"
}
```

### `tools/call` request

```http
POST /mcp/approved
Content-Type: application/json
Accept: application/json, text/event-stream

{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "greet",
    "arguments": { "name": "World" }
  }
}
```

### `tools/call` response

```http
HTTP/1.1 200 OK
Content-Type: text/event-stream

event: message
data: {
  "result": {
    "content": [
      { "type": "text", "text": "greet ok: {\"name\":\"World\"}" }
    ]
  },
  "id": 2,
  "jsonrpc": "2.0"
}
```

Errors are still HTTP 200, with `"isError": true` on the result object — they
are *not* JSON-RPC errors. (JSON-RPC errors at the protocol level — `-32005`
or others — are reserved for protocol-level problems like unknown method,
invalid params, or — relevant for the gateway — unapproved/missing server
definitions.)

## How to run the spike

```bash
cd spikes/mcp-sdk-tool-provider/McpToolProviderSpike
dotnet run --urls http://127.0.0.1:5139
```

Then in another shell:

```bash
# tools/list
curl -sN -X POST http://127.0.0.1:5139/mcp/approved \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'

# tools/call
curl -sN -X POST http://127.0.0.1:5139/mcp/approved \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"greet","arguments":{"name":"World"}}}'

# mutate the store (proves hot-reload)
curl -sX POST http://127.0.0.1:5139/admin/tools/listinvoices \
  -H 'Content-Type: application/json' \
  -d '{"description":"List invoices","inputSchema":"{\"type\":\"object\",\"properties\":{}}"}'
```

## What the plan got wrong

- `IMcpServerTool` does not exist. Use `WithListToolsHandler` +
  `WithCallToolHandler` (no custom type required).
- The plan's `app.MapMcp().WithDynamicTools(...)` is a non-existent fluent
  API. The real call is `app.MapMcp("/mcp/{serverName}")` after
  `WithListToolsHandler`/`WithCallToolHandler` are configured on the
  `IMcpServerBuilder`.
- The plan's `DynamicToolProvider : IMcpServerTool` and `DynamicMcpServerOptions`
  classes are not needed. The handler-based pattern is the SDK's native
  extension point.
- The plan's `MapGet("/mcp/{serverName}", ...)` placeholder is wrong — the
  real `MapMcp` extension handles the entire Streamable HTTP transport
  (POST, GET, DELETE on the same path) and we must not stub it.
