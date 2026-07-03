# Auth injection — three strategies with OBO as default for internal APIs

The gateway authenticates to underlying APIs using one of three strategies,
configured per API Instance. The strategy is chosen based on the API's security
requirements and the IdP relationship between the gateway and the API.

## Strategies

1. **OBO token exchange (default for internal company APIs)** — the gateway
   takes the caller's Entra ID JWT and exchanges it for a scoped M2M
   (machine-to-machine) token for the target API via the Entra ID
   On-Behalf-Of flow. The exchanged token is carried in the Authorization
   header by the MCP tool handler's HttpClient. Per-user attribution is
   preserved end-to-end — the API knows which human/app initiated the call.

   Flow: Caller (Entra ID JWT) → Gateway (validates) → OBO Handler (exchanges
   token for `api://target-api/.default` scope) → MCP Tool Handler (HttpClient
   with exchanged token) → API Destination (receives scoped M2M token).

2. **Token passthrough** — the caller's Entra ID token is forwarded directly
   to the API. Only works when the gateway and API share the same Entra ID
   app registration (same audience claim). Simpler than OBO but less flexible.

3. **Static credentials** — gateway stores an API key or bearer token per API
   Instance. Every tool call uses the same credential regardless of caller.
   Used for external APIs, legacy APIs, or APIs that don't support Entra ID.

**Considered Options:**
- OBO only — rejected. External APIs and legacy APIs can't use Entra ID.
- Static only — rejected. Loses per-user attribution, which the company requires for audit.
- All three (chosen) — covers internal (OBO), same-IdP (passthrough), and external/legacy (static).

**Consequences:**
- OBO handler is a core gateway component — uses Microsoft.Identity.Web or manual token exchange via Entra ID endpoint
- Exchanged tokens are cached with TTL (in-memory LRU). Synchronous fallback on cache miss (~50-100ms added latency)
- The OpenAPI security scheme informs the gateway which auth strategy is applicable, but the admin explicitly configures the strategy per server definition
- Auth injection happens in a DelegatingHandler on the HttpClient pipeline — the MCP tool handler code is unaware of auth (pure: tool name + args → HTTP request → response)
- Audit trail records: caller identity, auth strategy used, target API, tool name, arguments, response
- OBO requires the gateway to be registered as an app in Entra ID with API permissions for each target API