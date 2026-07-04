# `tests/McpGateway.IntegrationTests` — Agent Context

## Scope

HTTP-level integration tests using ASP.NET Core's `WebApplicationFactory`. They spin the full `McpGateway.Api` host in memory, exercise the MCP and admin endpoints, and use `Testcontainers.PostgreSQL` for the database.

## Test Patterns

- `WebApplicationFactory<Program>` subclass in `CustomWebApplicationFactory.cs`.
- `HttpClient` is used to call `/admin/servers/*` and `/mcp/{serverName}`.
- PostgreSQL container is started once per test class or fixture (see collection definitions).
- Auth is bypassed in integration tests by using the `DevelopmentAdmin` scheme or test-specific authentication setup.

## Common Helpers

- `TestAuthHandler` / test auth scheme registration (if present) — bypasses real Entra ID/API-key validation.
- OpenAPI spec fixtures are loaded from disk or inline strings.
- `ITestOutputHelper` is used for diagnostic logging.

## Collections

Integration tests are grouped into xUnit collections so expensive shared resources (Postgres container, factory) are reused:
- `IntegrationCollection.cs` (or similar) defines the shared fixture.

## Key Test Areas

| Area | Typical Files |
|---|---|
| Management API | `ServerManagementTests.cs`, `ApiKeyTests.cs` |
| MCP endpoint | `McpEndpointTests.cs` |
| Spec refresh flow | `SpecRefreshTests.cs` |
| Auth integration | `AuthenticationTests.cs` |

## Rules for This Directory

- Use `IClassFixture<CustomWebApplicationFactory>` or a shared collection fixture.
- Reset database state between tests via `Respawn` or fresh transactions.
- Prefer `HttpClient` assertions over testing internal services directly.
- Keep one logical scenario per test method.
- Use deterministic server names and tool names to avoid cross-test collisions.
