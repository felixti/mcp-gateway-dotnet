# MCP Gateway (.NET 10) — Data Model

## Storage split

| Store | What lives here | Why |
|-------|----------------|-----|
| PostgreSQL | MCP Server Definitions, tools, gateway API keys, tool overrides, spec versions | Persistent, queryable, relational |
| Azure Storage Queue | Audit events (fire-and-forget) | Same pipeline as Gateway Zero |
| In-memory (ConcurrentDictionary) | Hot-reload tool cache — loaded from PG on startup | Runtime truth for tools/list and tools/call |
| In-memory LRU | OBO exchanged token cache (TTL-based) | Avoid re-exchanging tokens on every call |

## ERD (ASCII)

```
┌──────────────────────┐       ┌──────────────────────┐
│ mcp_server_defs      │───1:N─│  tools               │
│  id                  │       │  id                  │
│  name (unique)       │       │  server_def_id       │
│  display_name        │       │  tool_name           │
│  description         │       │  description         │
│  spec_source_url     │       │  http_method         │
│  spec_content (JSONB)│       │  http_path           │
│  base_url             │       │  input_schema (JSONB)│
│  auth_strategy        │       │  output_schema (JSONB)│
│  auth_config (JSONB)  │       │  auth_config (JSONB) │
│  tool_mode           │       │  created_at          │
│  client_profile      │       │  updated_at          │
│  poll_interval_min   │       └──────────────────────┘
│  status              │
│  last_refreshed_at   │       ┌──────────────────────┐
│  created_at          │───1:N─│  tool_overrides      │
│  updated_at          │       │  id                  │
└────────┬─────────────┘       │  server_def_id       │
         │1:N                  │  tool_name           │
         │                     │  description_override│
         │                     │  visible (bool)      │
         │                     │  created_at          │
         │                     │  updated_at          │
         │                     └──────────────────────┘
         │
         │1:N
         │
┌────────▼─────────────┐       ┌──────────────────────┐
│  gateway_api_keys    │       │  spec_versions      │
│  id                  │       │  id                  │
│  server_def_id       │       │  server_def_id       │
│  key_hash            │       │  spec_hash           │
│  key_prefix          │       │  spec_content (JSONB)│
│  name                │       │  tool_count          │
│  scopes (TEXT[])     │       │  diff_summary (JSONB)│
│  created_at          │       │  created_at          │
│  revoked_at          │       └──────────────────────┘
│  last_used_at        │
└──────────────────────┘
```

NOTE: One gateway deployment per environment (dev/stg/prd). No cross-environment
routing. Each server definition has a single base_url + auth_config for the
environment it's deployed in. No `api_instances` table needed.

## Table definitions

### MCP Server Definitions

```sql
CREATE SCHEMA IF NOT EXISTS ai_gateway;

CREATE TABLE ai_gateway.mcp_server_defs (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name            TEXT NOT NULL UNIQUE,          -- used in /mcp/{name}
  display_name    TEXT NOT NULL,
  description     TEXT,
  spec_source_url  TEXT,                          -- URL to fetch spec (nullable if uploaded)
  spec_content    JSONB NOT NULL,                 -- latest parsed spec
  spec_hash       TEXT NOT NULL,                  -- SHA256 of spec content (for change detection)
  base_url        TEXT NOT NULL,                  -- API base URL for this environment
  auth_strategy   TEXT NOT NULL DEFAULT 'obo',    -- 'obo', 'passthrough', 'static'
  auth_config     JSONB NOT NULL DEFAULT '{}',    -- encrypted: client_id, client_secret, resource_scope (for OBO/SP), or api_key (for static)
  tool_mode       TEXT NOT NULL DEFAULT 'all',    -- 'all', 'dynamic', 'curated'
  client_profile  TEXT NOT NULL DEFAULT 'universal', -- 'universal', 'claude', 'cursor'
  poll_interval_minutes INT NOT NULL DEFAULT 1440, -- 24h default polling interval
  status          TEXT NOT NULL DEFAULT 'active',  -- 'active', 'disabled'
  approval_status TEXT NOT NULL DEFAULT 'pending', -- 'pending', 'approved', 'changes_pending'
  approved_at     TIMESTAMPTZ,                     -- last admin approval timestamp
  approved_by     TEXT,                            -- admin identity (Entra ID UPN)
  last_refreshed_at TIMESTAMPTZ,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

### Tools (generated from spec)

```sql
CREATE TABLE ai_gateway.tools (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  server_def_id   UUID NOT NULL REFERENCES mcp_server_defs(id) ON DELETE CASCADE,
  tool_name       TEXT NOT NULL,                  -- operationId or synthesized
  description     TEXT NOT NULL,                  -- from spec summary/description or synthesized
  http_method     TEXT NOT NULL,                  -- GET, POST, PUT, PATCH, DELETE
  http_path       TEXT NOT NULL,                  -- /invoices/{invoice_id}
  input_schema    JSONB NOT NULL,                 -- merged params + request body
  output_schema   JSONB,                          -- first 2xx response (nullable)
  auth_config     JSONB NOT NULL DEFAULT '{}',    -- per-tool auth overrides (from OpenAPI security scheme)
  visible         BOOLEAN NOT NULL DEFAULT true,  -- for curated mode: admin can hide
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE(server_def_id, tool_name)
);
```

### Tool Overrides (admin description edits, survives spec refresh)

```sql
CREATE TABLE ai_gateway.tool_overrides (
  id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  server_def_id       UUID NOT NULL REFERENCES mcp_server_defs(id) ON DELETE CASCADE,
  tool_name           TEXT NOT NULL,
  description_override TEXT NOT NULL,             -- admin's custom description
  visible             BOOLEAN NOT NULL DEFAULT true, -- for curated mode toggle
  created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
  UNIQUE(server_def_id, tool_name)
);
```

### Gateway API Keys (for MCP clients that can't do Entra ID)

```sql
CREATE TABLE ai_gateway.gateway_api_keys (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  server_def_id   UUID NOT NULL REFERENCES mcp_server_defs(id) ON DELETE CASCADE,
  key_hash        TEXT NOT NULL,                  -- bcrypt hash of the full key
  key_prefix      TEXT NOT NULL,                  -- first 8 chars for identification (mgk_abc1...)
  name            TEXT NOT NULL,                  -- human-readable label
  scopes          TEXT[] NOT NULL DEFAULT '{}',   -- server names this key can access
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  revoked_at      TIMESTAMPTZ,                    -- null = active
  last_used_at    TIMESTAMPTZ
);
```

### Spec Versions (audit history of spec changes)

```sql
CREATE TABLE ai_gateway.spec_versions (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  server_def_id   UUID NOT NULL REFERENCES mcp_server_defs(id) ON DELETE CASCADE,
  spec_hash       TEXT NOT NULL,                  -- SHA256 of spec at this version
  spec_content    JSONB NOT NULL,                 -- full spec snapshot
  tool_count      INT NOT NULL,                   -- number of tools generated
  diff_summary    JSONB NOT NULL DEFAULT '{}',    -- { added: [...], removed: [...], changed: [...] }
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

## Key indexes

```sql
-- Tool lookups (hot path — every tools/list and tools/call)
CREATE INDEX idx_tools_server_def ON ai_gateway.tools(server_def_id);
CREATE INDEX idx_tools_server_name ON ai_gateway.tools(server_def_id, tool_name);

-- Approved servers (hot-reload only loads approved server definitions)
CREATE INDEX idx_mcp_server_defs_approval ON ai_gateway.mcp_server_defs(approval_status) WHERE approval_status = 'approved';

-- Gateway API key lookup (auth hot path)
CREATE INDEX idx_gateway_api_keys_hash ON ai_gateway.gateway_api_keys(key_hash) WHERE revoked_at IS NULL;
CREATE INDEX idx_gateway_api_keys_prefix ON ai_gateway.gateway_api_keys(key_prefix) WHERE revoked_at IS NULL;

-- Tool overrides
CREATE INDEX idx_tool_overrides_server_tool ON ai_gateway.tool_overrides(server_def_id, tool_name);

-- Spec versions history
CREATE INDEX idx_spec_versions_server ON ai_gateway.spec_versions(server_def_id, created_at DESC);
```

## EF Core entities

The EF Core entities map directly to the tables above, all under the `ai_gateway`
schema. The DbContext configures the default schema:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDefaultSchema("ai_gateway");
    // entity configurations...
}
```

```csharp
public class McpServerDefinitionEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? SpecSourceUrl { get; set; }
    public string SpecContent { get; set; } = null!; // JSONB as string
    public string SpecHash { get; set; } = null!;
    public string BaseUrl { get; set; } = null!;
    public string AuthStrategy { get; set; } = "obo";
    public string AuthConfig { get; set; } = "{}"; // JSONB as string
    public string ToolMode { get; set; } = "all";
    public string ClientProfile { get; set; } = "universal";
    public int PollIntervalMinutes { get; set; } = 1440;
    public string Status { get; set; } = "active";
    public string ApprovalStatus { get; set; } = "pending"; // 'pending', 'approved', 'changes_pending'
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? LastRefreshedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ICollection<ToolEntity> Tools { get; set; } = [];
    public ICollection<ToolOverrideEntity> ToolOverrides { get; set; } = [];
    public ICollection<GatewayApiKeyEntity> GatewayApiKeys { get; set; } = [];
    public ICollection<SpecVersionEntity> SpecVersions { get; set; } = [];
}
```

## Table count: 5

|| Table | Purpose |
|-------|---------|
| mcp_server_defs | Registered MCP server definitions (one per OpenAPI spec) — includes base_url + auth per environment |
| tools | Generated tools per server definition |
| tool_overrides | Admin description overrides + curated mode visibility |
| gateway_api_keys | API keys for MCP clients that can't do Entra ID |
| spec_versions | Spec snapshot history with diff summary |

## What is NOT in PostgreSQL

- **OBO exchanged tokens** → in-memory LRU cache (TTL-based, refreshed before expiry)
- **In-memory tool cache** → ConcurrentDictionary (loaded from PG on startup, only
  server definitions with `approval_status = 'approved'`; updated on refresh + admin approval)
- **Audit content** → Azure Storage Queue → Blob Storage (same pipeline as Gateway Zero)
- **Runtime tool handlers** → in-memory only (constructed from ToolDefinition + HttpClient at call time)

NOTE: No `api_instances` table — one gateway per environment (dev/stg/prd), base_url and
auth_config live directly on `mcp_server_defs`. No cross-environment routing.