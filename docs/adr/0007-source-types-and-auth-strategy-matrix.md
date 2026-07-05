# Source types (OpenAPI + upstream MCP) and a two-strategy auth matrix

The gateway was born OpenAPI-only: one spec → one MCP Server Definition, tools
proxied as HTTP calls. We now also ingest **already-existing MCP servers** —
internal ones (e.g. Payments) and third-party ones (Azure DevOps, Databricks
Genie, ServiceNow, Salesforce) — so the gateway can re-host their tool catalogs
behind one approval / audit / auth / telemetry plane. And in production the
gateway sits behind an **AI gateway** (topology: client → AI gateway → MCP
gateway → upstream) that forwards a **user-attributed Entra assertion**
identifying either a human or a service principal.

## Decision

**1. Source type discriminator.** `McpServerDefinition` gains
`SourceType: openapi | mcp-upstream`. One table, one entity, one approval /
audit / telemetry pipeline. An `openapi` server generates tools from a spec
(status quo); an `mcp-upstream` server imports tools by calling the upstream's
`tools/list` and forwards `tools/call` as JSON-RPC. No new tables;
`ToolDefinition` carries either `(HttpMethod, HttpPath)` (openapi) or an
upstream-tool reference (mcp-upstream), dispatched behind a small
`IToolInvocationStrategy` so `ToolCallHandler` stays branch-free.

**2. Re-host, don't transparent-proxy.** For `mcp-upstream` the gateway
materializes the tool catalog (so ADR-0005 approval and `ToolMode: curated`
still work) and forwards each call live. Transparent session forwarding was
rejected — it discards approval, curation, auth injection, and per-tool audit.

**3. Two auth strategies, vendor-agnostic.** `obo` (default for any
Entra-registered target — internal APIs, Azure DevOps, Databricks Genie when
Entra-federated, ServiceNow when Entra-federated at the API edge) and
`credential` (stored token or OAuth client-credentials for non-federated
targets — Salesforce, unfederated ServiceNow / Databricks). The engine never
knows the vendor; only `AuthConfig` differs. The old `passthrough` strategy
folds into `obo` (degenerate case: audience already matches, skip the exchange)
and is retained as a documented alias for back-compat. The old `static`
strategy widens into `credential` (covers raw tokens AND OAuth
client-credentials with refresh).

**4. Inbound identity contract (topology a).** The MCP gateway authenticates
the **AI gateway** (service-to-service: Entra client-credentials JWT or gateway
API key — both already exist). The **human or service principal** rides inside
as a propagated Entra assertion, audience'd to the MCP gateway's own Entra
app. The gateway OBOs from that assertion to each upstream target.
**Audience = MCP gateway is non-negotiable** — a token that carries great user
claims but is audience'd to the AI gateway is *readable* (audit works) but
*not OBO-able*.

**5. Gateway-side audit attribution is universal.** Because the propagated
principal is known at the gateway regardless of upstream, the Audit Trail
records the originating human / service principal for *every* source type and
*every* auth strategy — including `credential`, where upstream attribution is
lost but gateway attribution is preserved.

## Considered Options

- **mcp-upstream modeling** — discriminator on the existing entity (chosen) vs.
  separate entity / table. Rejected separate entity: ADR-0003 hot-reload,
  ADR-0005 approval, and the audit span all key off the definition, not the
  source type; splitting duplicates that machinery.
- **mcp-upstream semantics** — re-host catalog (chosen) vs. transparent proxy
  vs. hybrid. Rejected transparent proxy: loses approval / curation /
  auth-injection / per-tool audit (half the ADRs). Hybrid collapses to re-host.
- **Auth to third-parties** — per-user OBO where the target is Entra-registered
  (chosen) vs. blanket M2M-everywhere. Rejected blanket M2M: throws away
  per-user attribution for Databricks / ServiceNow that *can* accept Entra
  tokens, for no engineering savings (same engine, different scope).
- **Auth-strategy taxonomy** — two strategies `obo` / `credential` (chosen) vs.
  four (`obo` / `passthrough` / `static` / `m2m-oauth`). Rejected four:
  `passthrough` is a degenerate `obo`, and `static` vs OAuth-client-credentials
  differ only in acquisition mechanics, best modeled as one `credential`
  strategy whose `AuthConfig` shape selects the mechanism.
- **Databricks second token exchange** — skip it (chosen). A second,
  Databricks-specific token-endpoint hop is unnecessary when the workspace is
  Entra-federated (an Entra OBO token audience'd to the Databricks Azure AD app
  `2ff814a6-3304-4ab8-85cb-cd0e6f879c1d` is accepted directly). Only a
  non-federated workspace needs vendor-native OAuth, which falls into
  `credential` automatically. No vendor adapters.

## Consequences

- `McpServerDefinition` gains `SourceType`; `ToolDefinition` gains an
  upstream-tool-reference shape. Migration is additive (nullable columns /
  discriminator defaulting to `openapi`).
- The MCP C# SDK (currently server-side only via
  `ModelContextProtocol.AspNetCore`) must be used **client-side** to call
  upstream `tools/list` and `tools/call` for `mcp-upstream` servers — verify
  client support in the SDK before implementation.
- `AuthStrategy` values change: `obo`, `credential` (+ `passthrough` retained
  as a documented alias of `obo` for back-compat). Existing rows using
  `passthrough` / `static` migrate on read.
- `mcp-upstream` refresh = re-call `tools/list`, diff the catalog, set
  `changes_pending` (ADR-0005) — same approval loop as `openapi` spec refresh,
  different fetch mechanism.
- **Integration-validation risk:** ServiceNow's MCP server OAuth is
  Authorization-Code-only with no Dynamic Client Registration. We bypass it by
  presenting an Entra-OBO bearer via ServiceNow's OIDC trust, but the exact
  token-acceptance semantics at the MCP edge must be validated before
  committing ServiceNow to the `obo` column.
- **Hard dependency on the AI gateway contract:** the forwarded assertion MUST
  be audience'd to the MCP gateway (or the AI gateway must exchange to mint
  one). This is an interface contract with the AI gateway team, recorded here
  as non-negotiable.
- **Deliberately deferred:** third-party tool-description / schema
  sanitization (prompt-injection surface from untrusted `description` /
  schemas fed to the LLM) is out of scope for this ADR and will be addressed
  separately — but it is a first-class requirement before any third-party
  `mcp-upstream` server ships to production.
