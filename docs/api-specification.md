# MCP Gateway (.NET 10) — API Specification

## MCP Consumer Endpoints

### POST /mcp/{server_name}

MCP Streamable HTTP endpoint. One per registered MCP Server Definition.

Auth: `Authorization: Bearer <Entra ID JWT>` OR `X-Gateway-Key: mgk_...`

Implements the MCP protocol (JSON-RPC 2.0 over Streamable HTTP):
- `initialize` — handshake
- `tools/list` — returns generated tools for this server
- `tools/call` — executes a tool by name with arguments
- `resources/list`, `resources/read` — if the spec declares any
- `prompts/list`, `prompts/get` — if the spec declares any

The gateway uses the C# MCP SDK's `MapMcp()` with a custom tool provider
backed by the InMemoryToolStore. Each `/mcp/{server_name}` route serves only
the tools from that server definition.

### Streamable HTTP transport details

The gateway exposes the 2025-06-18 MCP Streamable HTTP transport on a single
endpoint per server definition:

- **Base URL**: `POST /mcp/{server_name}`
- **Stateless mode**: enabled. No session ID is required or maintained.
- **Client → server**: send JSON-RPC 2.0 messages as the HTTP request body.
- **Server → client**: the server can stream responses back as
  Server-Sent Events (SSE) on the same POST request when the client sends
  `Accept: application/json, text/event-stream`. Alternatively, clients can
  open a separate `GET /mcp/{server_name}` request to receive an SSE stream.
- **Body size limit**: 10 MB (Kestrel default).
- **Content-Type**: `application/json` for JSON-RPC POST bodies.

This is the unified Streamable HTTP transport, not the older dedicated
`/sse` + `/message` endpoint pair.

### JSON-RPC error codes (gateway-level)

| Code | Meaning |
|------|---------|
| -32001 | Authentication failed (invalid JWT or API key) |
| -32002 | Server definition not found |
| -32003 | Tool not found |
| -32004 | Tool not visible (curated mode, admin hidden it) |
| -32005 | Server definition not approved (pending or changes_pending) |
| -32603 | Internal gateway error |

### Tool result errors (isError: true, not JSON-RPC errors)

API returns non-2xx → tool result with `isError: true`, content includes
`[HTTP {status}] {response body}`. Truncated to 10KB.

## Management API

Base URL: `/admin`
Auth: `Authorization: Bearer <Entra ID JWT>` with admin role claim.

### Server Definitions
- `GET    /admin/servers` — list (paginated, filter by status)
- `POST   /admin/servers` — register new server (upload spec or provide URL)
- `GET    /admin/servers/{name}` — get server definition
- `PATCH  /admin/servers/{name}` — update (display_name, description, tool_mode, client_profile, poll_interval, base_url, auth_strategy, auth_config, status)
- `DELETE /admin/servers/{name}` — delete (soft delete via status=disabled)
- `POST   /admin/servers/{name}/refresh` — trigger manual spec refresh
- `POST   /admin/servers/{name}/approve` — approve server definition (sets approval_status=approved, loads tools into hot store)
- `GET    /admin/servers/{name}/tools` — list generated tools (for admin review)
- `GET    /admin/servers/{name}/tools/diff` — diff current vs last-approved tool descriptions (shown when approval_status=changes_pending)
- `GET    /admin/servers/{name}/spec-versions` — spec change history

### Tool Management
- `GET    /admin/servers/{name}/tools` — list all tools
- `PATCH  /admin/servers/{name}/tools/{tool_name}` — update tool visibility (curated mode) or description override
- `PUT    /admin/servers/{name}/tools/{tool_name}/override` — set description override
- `DELETE /admin/servers/{name}/tools/{tool_name}/override` — remove override (revert to spec description)

### Gateway API Keys
- `GET    /admin/servers/{name}/api-keys` — list keys (hashes only, never full key)
- `POST   /admin/servers/{name}/api-keys` — issue new key (returns full key ONCE)
- `DELETE /admin/servers/{name}/api-keys/{key_id}` — revoke key

### Spec Management
- `POST   /admin/servers/{name}/spec` — upload spec file (multipart/form-data or JSON body)
- `PUT    /admin/servers/{name}/spec-source` — set spec source URL
- `GET    /admin/servers/{name}/spec` — get current spec content
- `GET    /admin/servers/{name}/spec/diff/{version_id}` — diff current vs historical version

## Health Endpoints

### GET /health

Liveness. Returns 200 if process is running.

```json
{"status": "ok", "uptime_seconds": 3600}
```

### GET /ready

Readiness. Returns 200 only if all dependencies are reachable:
- PostgreSQL connection
- Azure Storage Queue (optional — degrades to disk fallback)
- At least one MCP Server Definition loaded in InMemoryToolStore

```json
{
  "status": "ready",
  "checks": {
    "postgres": "ok",
    "storage_queue": "ok",
    "tool_store": "ok"
  }
}
```

If any check fails → 503 with `"status": "not_ready"`.

## Graceful Shutdown

On SIGTERM (K8s rollout/scale-down):

1. Stop accepting new connections
2. Set `/ready` to return 503 immediately
3. Wait for in-flight tool calls to complete (timeout: 30s)
4. Flush audit events from memory to Azure Storage Queue
5. Flush disk fallback buffer to queue
6. Close OBO token cache (no cleanup needed — in-memory only)
7. Close EF Core DbContext / connection pool
8. Exit cleanly

K8s `terminationGracePeriodSeconds`: 35s (30s drain + 5s cleanup).

If SIGKILL arrives (timeout exceeded): in-flight tool calls are lost.
Telemetry span shows incomplete call — detectable in monitoring.