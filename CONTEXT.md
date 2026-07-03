# MCP Gateway (.NET 10)

An MCP (Model Context Protocol) gateway built with .NET 10 that accepts any web
API's OpenAPI specification, dynamically generates an MCP Server definition from
it, and proxies MCP tool calls to the underlying API via HttpClient + Polly. The
gateway is the control plane — it parses specs, generates tools, manages auth
injection (OBO/passthrough/static), and routes calls. YARP is used only for the
management API surface. The underlying APIs are unchanged.

## Language

### Access Control

**Auth Strategy**:
The method the gateway uses to authenticate to an underlying API Instance. Three strategies, configured per API Instance: (1) OBO token exchange (default for internal company APIs) — the gateway takes the caller's Entra ID token and exchanges it for a scoped M2M (machine-to-machine) token for the target API via Entra ID OBO flow. The MCP tool handler carries the exchanged token in the Authorization header. Per-user attribution preserved end-to-end. (2) Token passthrough — the caller's Entra ID token is forwarded directly to the API. Only works when both gateway and API share the same Entra ID app registration. (3) Static credentials — gateway stores API key or bearer token per API Instance. Every tool call uses the same credential. No per-user attribution.
_Avoid_: Auth method, auth type, credential type

**Gateway Auth**:
The method MCP clients use to authenticate to the gateway itself. Two paths: (1) Entra ID JWT — for clients that support bearer tokens (Claude Code, custom apps, Codex CLI). Gateway validates against JWKS. (2) Gateway API key — for MCP clients that don't support bearer tokens (Claude Desktop, Cursor). Keys are scoped to specific MCP Server Definitions (a key for `/mcp/invoice` cannot access `/mcp/user`). Pragmatic hybrid: use Entra ID where possible, API keys where necessary.
_Avoid_: Client auth, gateway authentication, MCP auth

**OBO Handler**:
The gateway component that performs the Entra ID On-Behalf-Of token exchange. Takes the caller's JWT, validates it, requests a new access token scoped to the target API's resource (e.g., `api://invoice-api/.default`) via the OBO flow. The exchanged token is cached with TTL (Redis or in-memory) and refreshed before expiry. Synchronous fallback on cache miss (same pattern as Gateway Zero ADR-0005).
_Avoid_: Token exchanger, token service

### Core Concepts

**MCP Server Definition**:
A set of MCP tools generated from a single OpenAPI specification. One OpenAPI spec = one MCP Server Definition. Each definition contains tools, routing config, and auth configuration. Stored in PostgreSQL (original spec as JSONB + derived tool definitions as JSONB), loaded at gateway startup, hot-addable at runtime. On refresh, the gateway diffs old vs new spec, updates derived definitions, and retains the latest spec version for audit and re-generation. New server definitions and spec-refresh changes require admin approval before tools are served to MCP clients — the gateway returns JSON-RPC error -32005 for unapproved server definitions.
_Avoid_: MCP server (ambiguous — could mean the runtime process or the definition)

**Tool**:
A single MCP tool generated from one (path, method) pair in an OpenAPI specification. Has a name, description, input schema (JSON Schema), output schema (first 2xx response), and a handler that proxies to the underlying API endpoint via HttpClient. Tool exposure is configurable per MCP Server Definition via Tool Mode: `all` (expose every operation as a tool, default), `dynamic` (meta-tools pattern — list/get/invoke for large APIs >40 operations), `curated` (admin selects which operations become tools, excludes admin/debug/deprecated endpoints).
_Avoid_: Endpoint, operation, function

**Tool Mode**:
Per-server configuration that controls how operations from the OpenAPI spec are exposed as MCP tools. Three modes: `all` — every (path, method) pair becomes a tool (default, suitable for <40 operations). `dynamic` — three meta-tools (list_api_endpoints, get_api_endpoint_schema, invoke_api_endpoint) that let the LLM discover and invoke on demand (for large APIs, 40+ operations). `curated` — admin explicitly selects which operations become tools via the management API, others are hidden.
_Avoid_: Tool strategy, tool selection, exposure mode

**Spec Source**:
The OpenAPI document (file or URL) that the gateway parses to generate an MCP Server Definition. Must be OpenAPI 3.0+ (JSON or YAML). The spec is the single source of truth — when it changes, the MCP Server Definition is regenerated.
_Avoid_: API definition, swagger file, contract

**Client Profile**:
A per-server configuration that determines how generated tool schemas are transformed to match a specific MCP client's limitations. Values: `universal` (default — inline all $refs, split anyOf into separate tools, truncate names to 60 chars, works everywhere), `claude` (full schemas, supports anyOf at root), `cursor` (strict — 40 tool limit, 60-char names, no $refs, no anyOf). Set by admin per MCP Server Definition.
_Avoid_: Client mode, schema mode, compatibility mode

**API Instance**:
(Removed — one gateway deployment per environment: dev, stg, prd. No
cross-environment routing. The base_url and auth_config live directly on the
MCP Server Definition. Each environment has its own gateway instance with its
own database, so each server definition is environment-scoped.)
_Avoid_: Backend, upstream, server, replica, environment (use the specific
environment name: dev, stg, prd)

### Audit

**Audit Trail**:
Every MCP tool call is logged with: caller identity, gateway auth method (Entra ID or API key), auth strategy used (OBO/passthrough/static), target API Instance, tool name, arguments, response (truncated to 10KB), HTTP status, latency, timestamp. Emitted to Azure Storage Queue via fire-and-forget (same pipeline as Gateway Zero). At-least-once delivery with local disk fallback if queue is unavailable. Required because AI-driven actions (e.g., agent calling `delete_invoice`) must be auditable.
_Avoid_: Tool log, call log, MCP log

### Observability

**Telemetry**:
OpenTelemetry traces, metrics, and logs emitted via OTLP/HTTP to Dynatrace (same pipeline as Gateway Zero). Each tool call is a span: `mcp.tools.call {tool_name}` → `http.request {method, path}` → `obo.token_exchange` (if applicable). Metrics include tool call count, latency histogram, error rate, cache hit rate. Uses OpenTelemetry.Extensions.AspNetCore package.
_Avoid_: Monitoring, observability, logging (too broad)