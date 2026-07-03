# One gateway per environment — no cross-environment routing

The company enforces strict environment isolation: dev, stg, prd are
completely separate. No routing across environments. No shared databases.
No shared infrastructure.

## Decision

Each environment gets its own gateway deployment with its own PostgreSQL
database, Azure Storage Queue, OTLP endpoint, and API base URLs. The
`api_instances` table is eliminated. The `base_url`, `auth_strategy`, and
`auth_config` columns live directly on `mcp_server_defs`.

The gateway codebase is identical across environments. Configuration is
injected via environment variables / Azure Key Vault references in
appsettings.json. Each gateway only knows about the APIs in its own
environment.

**Considered Options:**
- Multi-instance table (api_instances with is_default, health checks, failover) — rejected. The company cannot cross environments. Multi-region within one environment is not a current requirement. Adds a table, background health-check service, failover logic, and instance selection complexity for no benefit.
- Single table with environment column (server_defs have env = dev/stg/prd) — rejected. Violates environment isolation — one gateway would have knowledge of other environments' base URLs and auth configs.
- Per-environment deployment with base_url on server definition (chosen) — simplest model. One server definition = one API in one environment. No instance selection, no health checks, no failover. The gateway is a control plane, not a load balancer.

**Consequences:**
- 6 tables → 5 tables (api_instances removed)
- No health-check background service for API instances
- No instance selection logic, no failover, no round-robin
- base_url and auth_config are per-server-definition, set by admin for the current environment
- Admin API PATCH includes base_url, auth_strategy, auth_config (admin sets these per environment)
- Docker-compose for local dev uses a single PG + Azurite + Jaeger
- AKS deployment via company CI/CD templates — one Helm release per environment
- If multi-region within one environment is needed in the future, re-introduce api_instances as a child table (but do not build it now)