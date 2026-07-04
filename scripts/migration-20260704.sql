CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ai_gateway') THEN
            CREATE SCHEMA ai_gateway;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE TABLE ai_gateway.mcp_server_defs (
        id uuid NOT NULL,
        name text NOT NULL,
        display_name text NOT NULL,
        description text,
        spec_source_url text,
        spec_content text NOT NULL,
        spec_hash text NOT NULL,
        base_url text NOT NULL,
        auth_strategy text NOT NULL,
        auth_config text NOT NULL DEFAULT '{}',
        tool_mode text NOT NULL DEFAULT 'all',
        client_profile text NOT NULL DEFAULT 'universal',
        poll_interval_minutes integer NOT NULL DEFAULT 1440,
        status text NOT NULL DEFAULT 'active',
        approval_status text NOT NULL DEFAULT 'pending',
        approved_at timestamp with time zone,
        approved_by text,
        last_refreshed_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_mcp_server_defs PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE TABLE ai_gateway.gateway_api_keys (
        id uuid NOT NULL,
        server_definition_id uuid NOT NULL,
        key_hash text NOT NULL,
        key_prefix text NOT NULL,
        name text NOT NULL,
        scopes text[] NOT NULL,
        created_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        last_used_at timestamp with time zone,
        CONSTRAINT pk_gateway_api_keys PRIMARY KEY (id),
        CONSTRAINT fk_gateway_api_keys_server_definitions_server_definition_id FOREIGN KEY (server_definition_id) REFERENCES ai_gateway.mcp_server_defs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE TABLE ai_gateway.spec_versions (
        id uuid NOT NULL,
        server_definition_id uuid NOT NULL,
        spec_hash text NOT NULL,
        spec_content text NOT NULL,
        tool_count integer NOT NULL,
        diff_summary text NOT NULL,
        created_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_spec_versions PRIMARY KEY (id),
        CONSTRAINT fk_spec_versions_server_definitions_server_definition_id FOREIGN KEY (server_definition_id) REFERENCES ai_gateway.mcp_server_defs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE TABLE ai_gateway.tool_overrides (
        id uuid NOT NULL,
        server_definition_id uuid NOT NULL,
        tool_name text NOT NULL,
        description_override text NOT NULL,
        visible boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_tool_overrides PRIMARY KEY (id),
        CONSTRAINT fk_tool_overrides_server_definitions_server_definition_id FOREIGN KEY (server_definition_id) REFERENCES ai_gateway.mcp_server_defs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE TABLE ai_gateway.tools (
        id uuid NOT NULL,
        server_definition_id uuid NOT NULL,
        tool_name text NOT NULL,
        description text NOT NULL,
        http_method text NOT NULL,
        http_path text NOT NULL,
        input_schema text NOT NULL,
        output_schema text,
        auth_config text NOT NULL DEFAULT '{}',
        visible boolean NOT NULL DEFAULT TRUE,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        CONSTRAINT pk_tools PRIMARY KEY (id),
        CONSTRAINT fk_tools_server_definitions_server_definition_id FOREIGN KEY (server_definition_id) REFERENCES ai_gateway.mcp_server_defs (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE INDEX ix_gateway_api_keys_key_prefix ON ai_gateway.gateway_api_keys (key_prefix) WHERE revoked_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE INDEX ix_gateway_api_keys_server_definition_id ON ai_gateway.gateway_api_keys (server_definition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE INDEX ix_mcp_server_defs_approval_status ON ai_gateway.mcp_server_defs (approval_status) WHERE approval_status = 'approved';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_mcp_server_defs_name ON ai_gateway.mcp_server_defs (name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE INDEX ix_spec_versions_server_definition_id_created_at ON ai_gateway.spec_versions (server_definition_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_tool_overrides_server_definition_id_tool_name ON ai_gateway.tool_overrides (server_definition_id, tool_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE INDEX ix_tools_server_definition_id ON ai_gateway.tools (server_definition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_tools_server_definition_id_tool_name ON ai_gateway.tools (server_definition_id, tool_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260704031232_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260704031232_InitialCreate', '10.0.9');
    END IF;
END $EF$;
COMMIT;

