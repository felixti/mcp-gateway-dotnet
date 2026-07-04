using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace McpGateway.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai_gateway");

            migrationBuilder.CreateTable(
                name: "mcp_server_defs",
                schema: "ai_gateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    spec_source_url = table.Column<string>(type: "text", nullable: true),
                    spec_content = table.Column<string>(type: "text", nullable: false),
                    spec_hash = table.Column<string>(type: "text", nullable: false),
                    base_url = table.Column<string>(type: "text", nullable: false),
                    auth_strategy = table.Column<string>(type: "text", nullable: false),
                    auth_config = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    tool_mode = table.Column<string>(type: "text", nullable: false, defaultValue: "all"),
                    client_profile = table.Column<string>(type: "text", nullable: false, defaultValue: "universal"),
                    poll_interval_minutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 1440),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "active"),
                    approval_status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    last_refreshed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mcp_server_defs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "gateway_api_keys",
                schema: "ai_gateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_hash = table.Column<string>(type: "text", nullable: false),
                    key_prefix = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    scopes = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gateway_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_gateway_api_keys_server_definitions_server_definition_id",
                        column: x => x.server_definition_id,
                        principalSchema: "ai_gateway",
                        principalTable: "mcp_server_defs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "spec_versions",
                schema: "ai_gateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    spec_hash = table.Column<string>(type: "text", nullable: false),
                    spec_content = table.Column<string>(type: "text", nullable: false),
                    tool_count = table.Column<int>(type: "integer", nullable: false),
                    diff_summary = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spec_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_spec_versions_server_definitions_server_definition_id",
                        column: x => x.server_definition_id,
                        principalSchema: "ai_gateway",
                        principalTable: "mcp_server_defs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tool_overrides",
                schema: "ai_gateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_name = table.Column<string>(type: "text", nullable: false),
                    description_override = table.Column<string>(type: "text", nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tool_overrides", x => x.id);
                    table.ForeignKey(
                        name: "fk_tool_overrides_server_definitions_server_definition_id",
                        column: x => x.server_definition_id,
                        principalSchema: "ai_gateway",
                        principalTable: "mcp_server_defs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tools",
                schema: "ai_gateway",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    server_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tool_name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    http_method = table.Column<string>(type: "text", nullable: false),
                    http_path = table.Column<string>(type: "text", nullable: false),
                    input_schema = table.Column<string>(type: "text", nullable: false),
                    output_schema = table.Column<string>(type: "text", nullable: true),
                    auth_config = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    visible = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tools", x => x.id);
                    table.ForeignKey(
                        name: "fk_tools_server_definitions_server_definition_id",
                        column: x => x.server_definition_id,
                        principalSchema: "ai_gateway",
                        principalTable: "mcp_server_defs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gateway_api_keys_key_prefix",
                schema: "ai_gateway",
                table: "gateway_api_keys",
                column: "key_prefix",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_gateway_api_keys_server_definition_id",
                schema: "ai_gateway",
                table: "gateway_api_keys",
                column: "server_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_server_defs_approval_status",
                schema: "ai_gateway",
                table: "mcp_server_defs",
                column: "approval_status",
                filter: "approval_status = 'approved'");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_server_defs_name",
                schema: "ai_gateway",
                table: "mcp_server_defs",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_spec_versions_server_definition_id_created_at",
                schema: "ai_gateway",
                table: "spec_versions",
                columns: new[] { "server_definition_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tool_overrides_server_definition_id_tool_name",
                schema: "ai_gateway",
                table: "tool_overrides",
                columns: new[] { "server_definition_id", "tool_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tools_server_definition_id",
                schema: "ai_gateway",
                table: "tools",
                column: "server_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_tools_server_definition_id_tool_name",
                schema: "ai_gateway",
                table: "tools",
                columns: new[] { "server_definition_id", "tool_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gateway_api_keys",
                schema: "ai_gateway");

            migrationBuilder.DropTable(
                name: "spec_versions",
                schema: "ai_gateway");

            migrationBuilder.DropTable(
                name: "tool_overrides",
                schema: "ai_gateway");

            migrationBuilder.DropTable(
                name: "tools",
                schema: "ai_gateway");

            migrationBuilder.DropTable(
                name: "mcp_server_defs",
                schema: "ai_gateway");
        }
    }
}
