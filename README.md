# MCP Gateway (.NET 10)

An MCP (Model Context Protocol) gateway that takes any web API's OpenAPI
specification, dynamically generates MCP tools, and proxies calls via
HttpClient + Polly. Built for financial company infrastructure: Entra ID auth,
full audit trail, admin approval workflow, per-environment deployment.

## Why

AI assistants (Claude, Cursor, Codex) use MCP to discover and invoke tools.
Building MCP server definitions by hand for every internal API is
unsustainable. This gateway automates it: register an OpenAPI spec, approve the
generated tools, and MCP clients can immediately use them — no code on the API
side, no MCP SDK integration, no per-API server process.

## How it works

1. Admin registers an OpenAPI spec (URL or file upload) via the management API
2. Gateway parses the spec and generates one MCP tool per (path, method) pair
3. Admin reviews tool descriptions (prompt injection defense) and approves
4. MCP clients connect to `/mcp/{server_name}` and call tools
5. Gateway proxies each call to the underlying API with auth injection
6. Every call is audited and traced

## Stack

- .NET 10 / ASP.NET Core 10 / C# 13
- ModelContextProtocol.AspNetCore (C# MCP SDK)
- Microsoft.OpenApi (OpenAPI 3.0+ parser)
- EF Core 10 + PostgreSQL 18
- Polly (retry, circuit breaker, timeout)
- Microsoft.Identity.Web (Entra ID JWT + OBO)
- Azure Storage Queue (audit) + OpenTelemetry → Dynatrace
- xUnit + Testcontainers + Reqnroll (BDD)

## Quick start (local dev)

```bash
# Clone
git clone https://github.com/felixti/mcp-gateway-dotnet.git
cd mcp-gateway-dotnet

# Start dependencies (PG, Azurite, Jaeger)
docker compose up -d

# Apply migrations (dev DB has full DDL)
dotnet ef database update --project src/McpGateway.Persistence

# Run the gateway
dotnet run --project src/McpGateway.Api

# Register a server definition
curl -X POST http://localhost:5000/admin/servers \
  -H "Authorization: Bearer <admin-jwt>" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "invoice-api",
    "spec_source_url": "https://invoice-api.example.com/openapi.json",
    "base_url": "https://invoice-api.example.com",
    "auth_strategy": "obo",
    "auth_config": {"resource": "api://invoice-api/.default"}
  }'

# Review generated tools
curl http://localhost:5000/admin/servers/invoice-api/tools \
  -H "Authorization: Bearer <admin-jwt>"

# Approve (tools go live)
curl -X POST http://localhost:5000/admin/servers/invoice-api/approve \
  -H "Authorization: Bearer <admin-jwt>"

# MCP clients can now connect to:
#   http://localhost:5000/mcp/invoice-api
```

## Documentation

- [Architecture](docs/architecture.md) — system diagram, request flows, deployment topology, graceful shutdown
- [Data Model](docs/data-model.md) — 5 PostgreSQL tables, ERD, EF Core entities
- [API Specification](docs/api-specification.md) — MCP endpoints, admin API, health, error codes
- [Project Structure](docs/project-structure.md) — 6 C# projects, horizontal layout, conventions
- [Testing Strategy](docs/testing-strategy.md) — shift-left, xUnit, Testcontainers, Reqnroll BDD
- [Domain Glossary](CONTEXT.md) — 10 terms across Access Control, Core Concepts, Audit, Observability

### ADRs

- [ADR-0001: HttpClient + Polly for tool proxying](docs/adr/0001-httpclient-polly-for-tool-proxying.md)
- [ADR-0002: Auth injection — three strategies, OBO default](docs/adr/0002-auth-injection-three-strategies-obo-default.md)
- [ADR-0003: Hot-reload tool registration via custom tool provider](docs/adr/0003-hot-reload-tool-registration.md)
- [ADR-0004: Migration via SQL script for DBA ticket process](docs/adr/0004-migration-sql-script-for-dba.md)

## Key decisions

- One gateway per environment (dev/stg/prd) — no cross-environment routing
- base_url + auth_config live directly on server definitions (no instance table)
- Admin approval required before tools go live (prompt injection defense)
- OBO token exchange as default auth for internal APIs (per-user attribution)
- Hot-reload via custom IMcpServerTool + ConcurrentDictionary (no restart on spec change)
- Migrations are SQL-scripted for DBA ticket process (production PG user is DML-only)
- Tool descriptions from spec only, admin override survives refresh (no LLM enhancement)

## Deployment

Deployed to AKS via company CI/CD templates. The gateway handles graceful
shutdown (SIGTERM → drain → flush audit → exit) and degradation (PG down → 503,
queue down → disk fallback, API down → Polly circuit breaker). See
[architecture.md](docs/architecture.md) for the full degradation matrix.

## License

Private repository. Internal use only.