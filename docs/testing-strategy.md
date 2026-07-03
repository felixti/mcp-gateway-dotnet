# MCP Gateway (.NET 10) — Testing Strategy

## Shift-left philosophy

Same approach as Gateway Zero: all quality gates automated and run locally before
code reaches CI. Three tiers of enforcement.

## Pre-commit hook (Husky.NET or pre-commit, <10s)

- dotnet format --verify-no-changes (format check)
- dotnet build --no-restore (compile check)
- Conventional commit message validation

## Pre-push hook (<60s)

- dotnet test --filter "Category=Unit" (unit tests only, no integration)
- dotnet format --verify-no-changes

## CI pipeline (GitHub Actions, runs on PR)

- Full test suite:
  - Unit tests (xUnit, plain C# — ToolGeneration, Proxy, Auth, ToolStore, SpecManagement)
  - Integration tests (xUnit + Testcontainers: real PostgreSQL, Azurite, Jaeger)
  - BDD tests (Reqnroll + xUnit, Gherkin .feature files for business scenarios)
- dotnet format --verify-no-changes (enforce formatting)
- dotnet build (all projects compile, no warnings as errors in CI)
- Security scan (Trivy or Snyk on Docker image)
- Docker build verification (multi-stage build succeeds)

## Testcontainers

| Service | Testcontainer image | Purpose |
|---------|-------------------|---------|
| PostgreSQL | postgres:18-alpine | MCP Server Definitions, tools, API instances, API keys |
| Azurite | mcr.microsoft.com/azure-storage/azurite | Audit queue emission |
| Jaeger | jaegertracing/all-in-one:1 | OTLP traces (Dynatrace-compatible protocol) |

## Mocking strategy (no real Entra ID in CI)

- Entra ID JWTs: generated locally using `Microsoft.IdentityModel.Tokens`
  (test signing key pair, test JWKS, test claims). No network call to Entra ID.
- OBO token exchange: mock the Entra ID token endpoint. Use
  `HttpMessageHandler` mock or WireMock.Net to intercept
  `login.microsoftonline.com` and return fake scoped tokens.
- Underlying API calls: mock via `HttpMessageHandler` in unit tests,
  WireMock.Net in integration tests. Return canned responses including
  paginated responses, errors (4xx/5xx), and large responses (for truncation tests).
- Real Entra ID and real APIs only hit in staging, never in CI.

## Test stack

| Test type | Tool | Gherkin | Purpose |
|-----------|------|---------|---------|
| Unit | xUnit | No | Tool generation, schema transform, HTTP request builder, response wrapper, auth resolver |
| Integration | xUnit + Testcontainers | No | Full MCP endpoint flow, admin API, spec refresh, OBO auth, audit emission |
| BDD | Reqnroll + xUnit | Yes | Business scenarios: "Generate tools from spec", "Proxy tool call with OBO auth", "Refresh spec detects new endpoints" |

## Gherkin scope

Gherkin .feature files are used ONLY for high-level business/acceptance scenarios
that product managers and compliance officers read. Unit and integration tests
stay plain xUnit. Example:

```gherkin
Feature: Tool generation from OpenAPI spec
  Scenario: Generate tools from a valid OpenAPI 3.0 spec
    Given an OpenAPI spec with 12 operations
    When the admin registers the spec
    Then 12 MCP tools are generated
    And each tool has a name, description, and input schema

  Scenario: Tool with missing operationId gets synthesized name
    Given an OpenAPI spec with operation "GET /users/{id}" without operationId
    When the admin registers the spec
    Then the tool is named "get_users_id"
```

## .NET BDD framework

- **Reqnroll** (successor to SpecFlow, actively maintained, .NET-native)
- xUnit as the underlying test runner
- `.feature` files in `McpGateway.BddTests/Features/`
- Step definitions in `McpGateway.BddTests/Steps/`

## Project test structure

```
tests/
├── McpGateway.UnitTests/           # xUnit, mirrors McpGateway.Core namespaces
├── McpGateway.IntegrationTests/    # xUnit + Testcontainers
└── McpGateway.BddTests/            # Reqnroll + xUnit + Gherkin
```