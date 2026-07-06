# Bruno and HTTP smoke-test guide

Test the MCP Gateway manually with the Bruno collection and the `smoke-test.http`
client in this repo. The smoke assets cover both supported source types:

- `openapi` — register Swagger Petstore v3, approve it, then call a generated
  HTTP-backed MCP tool.
- `mcp-upstream` — register an existing MCP server, import its `tools/list`
  catalog, approve it, then forward `tools/call` through the gateway.

## What you need

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://docs.docker.com/get-started/get-docker/)
- [Bruno](https://www.usebruno.com/downloads) or an editor that can run
  `.http` files
- `jq` (only if you also want to run `smoke-test.sh`)
- Optional for the `mcp-upstream` path: a Streamable HTTP MCP server reachable
  from the gateway. The examples assume `http://localhost:7001/mcp` with an
  `echo` tool accepting `{ "msg": string }`; change the variables/body if your
  upstream has a different URL or tool schema.

## Transport note: Streamable HTTP, not old SSE-only

The gateway exposes MCP over **Streamable HTTP**. The JSON-RPC requests are sent
with normal `POST /mcp/{serverName}` HTTP requests. Responses are delivered as
MCP stream events, so manual clients must send:

```http
Accept: application/json, text/event-stream
```

If either media type is missing, the MCP SDK rejects the request with
`Not Acceptable: Client must accept both application/json and text/event-stream`.

## 1. Start dependencies

From the repo root:

```bash
docker compose up -d postgres azurite jaeger
```

Wait until `postgres` and `jaeger` show `healthy`:

```bash
docker compose ps
```

## 2. Apply the database migrations

```bash
dotnet ef database update --project src/McpGateway.Persistence
```

## 3. Start the gateway

```bash
dotnet run --project src/McpGateway.Api
```

The API listens on `http://localhost:5121` in Development. Leave this terminal
open.

## 4. Open the Bruno collection

1. Open Bruno.
2. Click **Open Collection**.
3. Select the `bruno/McpGateway` folder in this repo.
4. Switch to the **Development** environment.

Default Development variables:

| Variable | Default | Purpose |
|---|---:|---|
| `baseUrl` | `http://localhost:5121` | Gateway base URL |
| `adminUpn` | `admin@example.com` | Development admin identity header value |
| `mcpApiKey` | secret, set by script | API key for the Petstore `openapi` flow |
| `upstreamServerName` | `upstream-smoke` | Server name for the `mcp-upstream` flow |
| `upstreamUrl` | `http://localhost:7001/mcp` | Upstream MCP Streamable HTTP endpoint |
| `upstreamToolName` | `echo` | Tool name used by the upstream tool-call request |
| `upstreamMcpApiKey` | secret, set by script | API key for the `mcp-upstream` flow |

## 5. OpenAPI smoke path: Petstore

Run these Bruno requests in order.

### Admin

1. **Validate Server** (`POST /admin/servers/validate`)
   - Dry-runs the Swagger Petstore v3 OpenAPI spec.
   - Expected: `200 OK` with empty `errors` for the current public Petstore
     spec. Warnings can exist; validation warnings do not block registration.

2. **Register Server** (`POST /admin/servers`)
   - Creates `petstore-smoke` from
     `https://petstore3.swagger.io/api/v3/openapi.json`.
   - Expected: `201 Created`, response body shows
     `sourceType: openapi` and `approvalStatus: pending`.

3. **Approve Server** (`POST /admin/servers/petstore-smoke/approve`)
   - Expected: `200 OK`, response shows `approvalStatus: approved` and
     `toolCount: 19`.

4. **Create API Key** (`POST /admin/servers/petstore-smoke/api-keys`)
   - Expected: `201 Created`.
   - A post-response script automatically saves `res.body.fullKey` into the
     `mcpApiKey` environment variable.

### MCP

5. **Initialize** (`POST /mcp/petstore-smoke`)
   - Expected: `200 OK` with an event stream line containing server info.

6. **Tools List** (`POST /mcp/petstore-smoke`)
   - Expected: `200 OK` with a `data:` line containing 19 tools
     (`addpet`, `findpetsbystatus`, etc.).

7. **Tools Call** (`POST /mcp/petstore-smoke`)
   - Calls `findpetsbystatus` with `{ "status": "available" }`.
   - Expected: `200 OK` with a `data:` line containing a JSON array of pets;
     `isError` is `false`.

## 6. MCP-upstream smoke path

This path requires a separate MCP server reachable at `upstreamUrl`. The gateway
will call the upstream server's `tools/list` during registration, persist the
catalog, and then forward gateway `tools/call` requests to the upstream server.

Do **not** run **Validate Server** for `mcp-upstream`; `/admin/servers/validate`
is OpenAPI-specific today.

Run these Bruno requests in order.

### Admin

1. **Register MCP Upstream Server** (`POST /admin/servers`)
   - Uses `sourceType: "mcp-upstream"`.
   - Sends both `upstreamUrl` and `specSourceUrl` with the same value. This is
     intentional: the current request validator still requires `specSourceUrl`
     or `specContent`, even though the mcp-upstream branch imports from
     `upstreamUrl`.
   - Expected: `201 Created`, response body shows
     `sourceType: mcp-upstream`, `upstreamUrl`, and `approvalStatus: pending`.

2. **Approve MCP Upstream Server**
   (`POST /admin/servers/{{upstreamServerName}}/approve`)
   - Expected: `200 OK`, response shows `approvalStatus: approved` and a
     `toolCount` equal to the upstream server's catalog size.

3. **Create MCP Upstream API Key**
   (`POST /admin/servers/{{upstreamServerName}}/api-keys`)
   - Expected: `201 Created`.
   - A post-response script saves `res.body.fullKey` into
     `upstreamMcpApiKey`.

### MCP Upstream

4. **Initialize MCP Upstream** (`POST /mcp/{{upstreamServerName}}`)
   - Expected: `200 OK` with an event stream line containing gateway MCP server
     info.

5. **Tools List MCP Upstream** (`POST /mcp/{{upstreamServerName}}`)
   - Expected: `200 OK` with a `data:` line containing the tools imported from
     the upstream MCP server.

6. **Tools Call MCP Upstream** (`POST /mcp/{{upstreamServerName}}`)
   - Defaults to calling `{{upstreamToolName}}` with
     `{ "msg": "hello from bruno" }`.
   - Expected: `200 OK` with a successful `CallToolResult` forwarded from the
     upstream server.
   - If your upstream tool has a different name or input schema, edit
     `upstreamToolName` and the request body's `arguments` object before
     running this request.

## 7. Alternative: `smoke-test.http`

If you prefer an editor HTTP client, open `smoke-test.http`. It contains the
same Petstore `openapi` flow followed by the `mcp-upstream` flow.

Manual API-key step:

1. Run the Petstore create-key request and copy `fullKey` into `@gatewayKey`.
2. Run the mcp-upstream create-key request and copy `fullKey` into
   `@upstreamGatewayKey`.

Then run the matching MCP requests.

## 8. Alternative: command-line script

If you prefer the terminal, run:

```bash
./smoke-test.sh
```

The script registers a uniquely named OpenAPI Petstore server, approves it,
creates an API key, and calls the MCP endpoint automatically. It does not start
or register a separate MCP upstream server.

## 9. Stop everything

In the gateway terminal press `Ctrl+C`, then:

```bash
docker compose down
```

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Connection refused` to `localhost:5121` | Gateway not started or still starting. | Wait for `Now listening on: http://localhost:5121`. |
| `Connection refused` to `upstreamUrl` during mcp-upstream registration | The upstream MCP server is not running or the URL points to the wrong route. | Start the upstream MCP server and verify its Streamable HTTP endpoint URL. |
| `Either SpecSourceUrl or SpecContent is required.` on mcp-upstream registration | Current create-server validation still enforces an OpenAPI-era field. | Keep `specSourceUrl` set to the same value as `upstreamUrl` in manual smoke requests. |
| `Cannot approve a disabled server definition.` | Server name was used before and soft-deleted. | Pick a new name in the register request, or update the existing server `status` to `active`. |
| `Not Acceptable: Client must accept both application/json and text/event-stream` | `Accept` header missing one of the two media types. | Keep `Accept: application/json, text/event-stream` on MCP requests. |
| `Invalid or unauthorized API key.` | API key variable not set or scoped to a different server. | Re-run the matching create-key request; Bruno stores `mcpApiKey` or `upstreamMcpApiKey` automatically. |
| `[-32005] server '<name>' is not approved` | Approval step skipped or failed. | Run the matching approve request again. |
| `Tool not found` on mcp-upstream tool call | `upstreamToolName` does not match the imported upstream catalog. | Run **Tools List MCP Upstream**, copy a tool name, and update `upstreamToolName`. |
