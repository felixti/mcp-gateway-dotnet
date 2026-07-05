# MCP Gateway (.NET 10)

An MCP (Model Context Protocol) gateway built with .NET 10 that turns backends
into MCP servers a client can call through one governed entrypoint. It accepts
either an OpenAPI 3.0+ specification (generating tools from each operation) or
an already-existing MCP server (re-hosting its tool catalog), then proxies MCP
tool calls to the underlying backend. The gateway is the control plane — it
parses specs / imports catalogs, generates tools, and manages auth injection
(OBO / credential), approval, audit, and telemetry. In production it sits
behind an AI gateway that forwards a user-attributed Entra assertion. The
underlying backends are unchanged.

## Language

### Access Control

**Auth Strategy**:
The method the gateway uses to authenticate to an upstream backend, configured per MCP Server Definition. Two strategies: (1) `obo` — the gateway takes the Propagated Principal's Entra assertion and exchanges it (Entra ID OBO flow) for a scoped token for the target's Entra-registered app, carried in the Authorization header. Works for any Entra-registered target — internal APIs, Azure DevOps, Databricks Genie (when Entra-federated), ServiceNow (when Entra-federated at the API edge). Per-user attribution preserved end-to-end. `passthrough` (forward the caller token directly, no exchange) is a degenerate `obo`, retained as a documented alias for back-compat. (2) `credential` — the gateway stores a token or OAuth client-credentials per definition and calls as itself (a service principal), used for non-federated targets (Salesforce, unfederated ServiceNow / Databricks). Upstream per-user attribution is lost; gateway-side audit attribution is still preserved.
_Avoid_: Auth method, auth type, credential type

**Gateway Auth**:
How the AI gateway authenticates to the MCP gateway (service-to-service), since the MCP gateway sits behind the AI gateway in production (topology: client → AI gateway → MCP gateway → upstream). Two paths: (1) Entra ID client-credentials JWT — the AI gateway presents its own Entra app token; the MCP gateway validates against JWKS. (2) Gateway API key — `X-Gateway-Key: mgk_…`, scoped to specific MCP Server Definitions. The human or service principal the call is made on behalf of rides inside as a Propagated Principal, not as the direct caller.
_Avoid_: Client auth, gateway authentication, MCP auth

**Propagated Principal**:
The human (Entra ID user) or service principal (Entra ID app registration) on whose behalf a call is made, propagated through the AI gateway to the MCP gateway as a user-attributed Entra assertion. Identified by claims in the assertion; recorded in the Audit Trail as the caller. Distinct from Gateway Auth, which authenticates the AI gateway itself. The assertion must be audience'd to the MCP gateway for the OBO Handler to exchange it.
_Avoid_: End user, caller (ambiguous — use Propagated Principal for the human / SP behind a call), upstream identity

**OBO Handler**:
The gateway component that performs the Entra ID On-Behalf-Of token exchange. Takes the Propagated Principal's assertion (a human's user JWT or a service principal's client assertion), validates it, and requests a new access token scoped to the target backend's Entra app (e.g. `api://invoice-api/.default`, or the Databricks Azure AD app) via the OBO flow. The exchanged token is cached with TTL and refreshed before expiry; synchronous fallback on cache miss. Works for any Entra-registered target; the engine is vendor-agnostic and driven entirely by the scope in AuthConfig.
_Avoid_: Token exchanger, token service

### Core Concepts

**MCP Server Definition**:
A managed set of MCP tools exposed by the gateway at `/mcp/{server_name}`. Has a Source Type that determines where its tools come from — `openapi` (generated from an OpenAPI 3.0+ specification) or `mcp-upstream` (imported by re-hosting an existing MCP server's tool catalog). Each definition contains tools, routing config, and auth configuration. Stored in PostgreSQL (source content + derived tool definitions as JSONB), loaded at gateway startup, hot-addable at runtime. On refresh, the gateway diffs old vs new tools and retains the latest source version for audit. New definitions and refresh changes require admin approval before tools are served — the gateway returns JSON-RPC error -32005 for unapproved definitions.
_Avoid_: MCP server (ambiguous — could mean the runtime process, an upstream MCP server, or the definition)

**Source Type**:
The origin of a definition's tools. Values: `openapi` — tools generated from an OpenAPI 3.0+ Spec Source; `mcp-upstream` — tools imported by re-hosting an existing MCP server's catalog (internal MCP servers or third-party ones like Azure DevOps, Databricks Genie, ServiceNow, Salesforce). Determines the refresh mechanism (spec re-fetch vs `tools/list` re-call) and the invocation transport (HTTP vs JSON-RPC).
_Avoid_: Backend type, server kind, source mode

**Tool**:
A single MCP tool exposed by a definition. For `openapi` definitions, generated from one (path, method) pair — has HttpMethod / HttpPath and is proxied as an HTTP call. For `mcp-upstream` definitions, imported from the upstream server's `tools/list` — has a name + input schema and is forwarded as a JSON-RPC `tools/call`. Both shapes carry a description, input schema, output schema (where available), and visibility. Tool exposure is configurable per definition via Tool Mode.
_Avoid_: Endpoint, operation, function

**Tool Mode**:
Per-server configuration that controls how operations from the OpenAPI spec are exposed as MCP tools. Three modes: `all` — every (path, method) pair becomes a tool (default, suitable for <40 operations). `dynamic` — three meta-tools (list_api_endpoints, get_api_endpoint_schema, invoke_api_endpoint) that let the LLM discover and invoke on demand (for large APIs, 40+ operations). `curated` — admin explicitly selects which operations become tools via the management API, others are hidden.
_Avoid_: Tool strategy, tool selection, exposure mode

**Spec Source**:
The OpenAPI document (file or URL) that the gateway parses to generate tools for an `openapi` MCP Server Definition. Must be OpenAPI 3.0+ (JSON or YAML). The spec is the single source of truth for that definition — when it changes, the tools are regenerated. Not applicable to `mcp-upstream` definitions, whose source is the upstream server's live `tools/list`.
_Avoid_: API definition, swagger file, contract

**Client Profile**:
A per-server configuration that determines how generated tool schemas are transformed to match a specific MCP client's limitations. Values: `universal` (default — inline all $refs, split anyOf into separate tools, truncate names to 60 chars, works everywhere), `claude` (full schemas, supports anyOf at root), `cursor` (strict — 40 tool limit, 60-char names, no $refs, no anyOf). Set by admin per MCP Server Definition.
_Avoid_: Client mode, schema mode, compatibility mode

**Streamable HTTP Transport**:
The MCP 2025-06-18 transport used by the gateway for client connections. One endpoint per MCP Server Definition at `/mcp/{server_name}`. Clients send JSON-RPC 2.0 messages via HTTP POST; the gateway streams responses back as Server-Sent Events (SSE) on the same POST or on a separate GET to the same endpoint. Stateless mode is enabled, so no session ID is required.
_Avoid_: SSE endpoint, MCP endpoint, /sse endpoint

**API Instance**:
(Removed — one gateway deployment per environment: dev, stg, prd. No
cross-environment routing. The base_url and auth_config live directly on the
MCP Server Definition. Each environment has its own gateway instance with its
own database, so each server definition is environment-scoped.)
_Avoid_: Backend, upstream, server, replica, environment (use the specific
environment name: dev, stg, prd)

### Audit

**Audit Trail**:
Every MCP tool call is logged with: Propagated Principal identity (human or service principal) + type, gateway auth method (Entra ID or API key), auth strategy used (obo / credential), target backend, tool name, arguments, response (truncated to 10KB), HTTP status / JSON-RPC error, latency, timestamp. Emitted to Azure Storage Queue via fire-and-forget (same pipeline as Gateway Zero). At-least-once delivery with local disk fallback if queue is unavailable. Required because AI-driven actions (e.g., agent calling `delete_invoice`) must be auditable. Gateway-side attribution is recorded for every source type and auth strategy, including `credential` where upstream attribution is lost.
_Avoid_: Tool log, call log, MCP log

### Observability

**Telemetry**:
OpenTelemetry traces, metrics, and logs emitted via OTLP/HTTP to Dynatrace (same pipeline as Gateway Zero). Each tool call is a span: `mcp.tools.call {tool_name}` → `http.request {method, path}` → `obo.token_exchange` (if applicable). Metrics include tool call count, latency histogram, error rate, cache hit rate. Uses OpenTelemetry.Extensions.AspNetCore package.
_Avoid_: Monitoring, observability, logging (too broad)
