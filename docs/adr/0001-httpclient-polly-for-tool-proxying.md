# HttpClient + Polly for tool-call proxying — YARP only for management API

The original design proposed YARP as the proxy layer for MCP tool calls. On
investigation, the MCP tool-call flow doesn't fit YARP's request/response proxy
model. The incoming request is JSON-RPC (tools/call), not a raw HTTP request to
forward. The gateway must parse the JSON-RPC, extract arguments, construct a
NEW HTTP request (path params → URL, query params → query string, body →
request body), call the API, parse the response, and wrap it as an MCP
CallToolResult (JSON content blocks). This transformation belongs in the MCP
tool handler, not in a reverse proxy pipeline.

YARP is retained for the management API surface (admin CRUD endpoints, health
checks, OpenAPI spec upload) where it provides value as a standard reverse
proxy. For the tool-call proxy path, the gateway uses IHttpClientFactory +
Polly (retry, circuit breaker, timeout, health checks) — the standard .NET
pattern for outbound HTTP calls.

**Considered Options:**
- YARP routes per tool — rejected. YARP proxies full HTTP request/response, but the incoming request is JSON-RPC, not HTTP to forward. The handler must transform arguments → HTTP request, which can't happen inside YARP's pipeline.
- Hybrid (YARP clusters + handler uses HttpClient) — rejected. Duplicates configuration: YARP clusters for destinations + handler-side HttpClient. Two sources of truth for the same API Instance.
- HttpClient + Polly only (chosen) — one path, one config source. IHttpClientFactory provides named clients per API Instance. Polly handles retry, circuit breaker, timeout, health checks. Auth injection via DelegatingHandler (standard .NET pattern).

**Consequences:**
- Each registered API gets a named HttpClient via IHttpClientFactory
- Polly policies: retry (3 attempts, exponential backoff), circuit breaker (5 failures → open 30s), timeout (30s default, configurable per server definition)
- Auth injection via DelegatingHandler — the handler reads the resolved auth token from the request context and sets the Authorization header before forwarding
- YARP is still used for management API reverse proxy if needed, but is NOT in the tool-call critical path