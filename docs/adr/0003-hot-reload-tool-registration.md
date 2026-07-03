# Hot-reload tool registration via custom tool provider

The MCP C# SDK's primary pattern is `WithToolsFromAssembly()` — attribute-based,
compile-time tool registration. Tools are frozen at server startup. This does
not work for a gateway that dynamically generates tools from OpenAPI specs at
runtime and must add/remove tools without restarting.

The gateway implements a custom tool provider backed by PostgreSQL + in-memory
cache. The `tools/list` handler reads from this mutable store on every call.
When a spec is refreshed or a new server is added:

1. Update PostgreSQL (MCP Server Definition + tools)
2. Update the in-memory cache (ConcurrentDictionary)
3. Next `tools/list` call returns the updated tool set
4. No restart, no session disconnect, no disruption

**Considered Options:**
- Full hot-reload via custom tool provider (chosen) — tools mutate at runtime, active sessions pick up changes on next tools/list. Requires implementing IMcpServerTool or equivalent custom registration that delegates to the mutable store.
- Endpoint restart — disconnect active sessions, restart MCP endpoint with new tools. Simpler but disruptive; clients must reconnect.
- Process restart — restart the whole gateway. Unacceptable for production.

**Consequences:**
- Must implement a custom tool registration mechanism (not WithToolsFromAssembly)
- In-memory cache: ConcurrentDictionary<serverName, List<ToolDefinition>> — updated atomically on spec refresh
- tools/list reads from the in-memory cache, NOT from a frozen snapshot
- tools/call looks up the tool by name in the cache, retrieves the OpenAPI operation + API Instance config, constructs the HTTP request, and proxies via HttpClient
- The custom tool provider must be thread-safe (concurrent tools/list and tools/call while cache is being updated)
- On gateway startup: load all MCP Server Definitions from PostgreSQL into the in-memory cache before accepting MCP connections
- Risk: the C# SDK may not expose a clean extension point for mutable tool stores. If the SDK freezes tools at startup, a custom IMcpServerTool implementation is needed that delegates to our store. This must be verified during implementation.