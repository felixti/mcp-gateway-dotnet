# Bruno smoke-test guide

Test the MCP Gateway manually against the public Swagger Petstore v3 API using the Bruno collection in this repo.

## What you need

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://docs.docker.com/get-started/get-docker/)
- [Bruno](https://www.usebruno.com/downloads)
- `jq` (only if you also want to run `smoke-test.sh`)

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

The API listens on `http://localhost:5121` in Development. Leave this terminal open.

## 4. Open the Bruno collection

1. Open Bruno.
2. Click **Open Collection**.
3. Select the `bruno/McpGateway` folder in this repo.
4. Switch to the **Development** environment (`baseUrl` is already `http://localhost:5121`, `adminUpn` is `admin@example.com`).

## 5. Run the requests in order

The collection is organized under three folders. Run only these requests, in this order:

### Admin

1. **Register Server** (`POST /admin/servers`)
   - Creates `petstore-smoke` from `https://petstore3.swagger.io/api/v3/openapi.json`.
   - Expected: `201 Created`, response body shows `approvalStatus: pending`.

2. **Approve Server** (`POST /admin/servers/petstore-smoke/approve`)
   - Expected: `200 OK`, response shows `approvalStatus: approved` and `toolCount: 19`.

3. **Create API Key** (`POST /admin/servers/petstore-smoke/api-keys`)
   - Expected: `201 Created`.
   - A post-response script automatically saves `res.body.fullKey` into the `mcpApiKey` environment variable.

### MCP

4. **Initialize** (`POST /mcp/petstore-smoke`)
   - Expected: `200 OK` with an SSE `event: message` line containing server info.

5. **Tools List** (`POST /mcp/petstore-smoke`)
   - Expected: `200 OK` with an SSE `data:` line containing 19 tools (`addpet`, `findpetsbystatus`, etc.).

6. **Tools Call** (`POST /mcp/petstore-smoke`)
   - Calls `findpetsbystatus` with `{ "status": "available" }`.
   - Expected: `200 OK` with a `data:` line containing a JSON array of pets; `isError` is `false`.

## 6. Stop everything

In the gateway terminal press `Ctrl+C`, then:

```bash
docker compose down
```

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Connection refused` to `localhost:5121` | Gateway not started or still starting. | Wait for `Now listening on: http://localhost:5121`. |
| `Cannot approve a disabled server definition.` | Server name was used before and soft-deleted. | Pick a new name in the register request, or update the existing server `status` to `active`. |
| `Not Acceptable: Client must accept both application/json and text/event-stream` | `Accept` header missing one of the two media types. | The Bruno requests already include both; do not remove one. |
| `Invalid or unauthorized API key.` | `mcpApiKey` not set or scoped to a different server. | Re-run **Create API Key**; the post-response script sets the variable automatically. |
| `[-32005] server 'petstore-smoke' is not approved` | Approval step skipped or failed. | Run **Approve Server** again. |

## Alternative: command-line script

If you prefer the terminal, run:

```bash
./smoke-test.sh
```

The script registers a uniquely-named server, approves it, creates an API key, and calls the MCP endpoint automatically.
