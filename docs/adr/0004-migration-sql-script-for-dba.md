# Migration workflow — SQL script generation for DBA ticket process

The production PostgreSQL user has DML grants only (INSERT, UPDATE, DELETE,
SELECT) — no DDL grants (CREATE TABLE, ALTER TABLE, CREATE INDEX). The gateway
MUST NOT run EF Core migrations at startup in production. Instead, migrations
are converted to SQL scripts and submitted to the DBA team for manual execution.

## Workflow

### Local dev / CI (full DDL access)

```bash
# Apply migrations directly (dev DB has full grants)
dotnet ef database update --project src/McpGateway.Persistence
```

### Production (DML-only, no DDL)

```bash
# Generate SQL script from migration
dotnet ef migrations script --project src/McpGateway.Persistence --output scripts/migration-$(date +%Y%m%d).sql

# Or generate script for a specific migration range
dotnet ef migrations script --project src/McpGateway.Persistence --from <last-applied> --to <latest> --output scripts/migration.sql
```

The generated SQL script is:
1. Reviewed by a developer
2. Attached to a DBA ticket
3. DBA team reviews and executes the script manually
4. Gateway deployment proceeds after DBA confirms migrations are applied

## Idempotency

EF Core's `migrations script` generates SQL with `IF EXISTS` / `IF NOT EXISTS`
checks where applicable, but some statements are not idempotent by default.
The `--idempotent` flag generates a script that checks migration history before
applying:

```bash
dotnet ef migrations script --idempotent --project src/McpGateway.Persistence --output scripts/migration.sql
```

This is the recommended flag for DBA-submitted scripts — if the script is run
twice by accident, it won't error on already-applied migrations.

**Considered Options:**
- Run EF Core migrations at startup (database.Migrate()) — rejected. Production PG user has no DDL grants.
- EF Core bundler ( generates a self-contained .exe) — rejected. Still requires DDL grants at runtime.
- SQL script generation (chosen) — decouples migration from deployment. DBA controls when and how DDL runs.

**Consequences:**
- `dotnet ef migrations script --idempotent` is the production migration command
- All tables are under the `ai_gateway` schema (configured via `modelBuilder.HasDefaultSchema("ai_gateway")` in DbContext)
- The DBA ticket must include `CREATE SCHEMA IF NOT EXISTS ai_gateway` as the first statement if the schema doesn't exist yet
- Scripts are stored in `scripts/` directory and version-controlled
- Each script is attached to a DBA ticket with a review checklist
- The gateway checks for pending migrations on startup and logs a warning if the database schema is behind the code
- Local dev: `dotnet ef database update` runs directly
- CI (Testcontainers): `dotnet ef database update` runs directly (full DDL access)
- A `npm run db:migrate:sql` equivalent for .NET: a shell script that wraps `dotnet ef migrations script --idempotent` and outputs a timestamped .sql file