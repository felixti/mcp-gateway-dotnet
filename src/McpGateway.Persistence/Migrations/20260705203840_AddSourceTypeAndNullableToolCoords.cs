using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace McpGateway.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceTypeAndNullableToolCoords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "http_path",
                schema: "ai_gateway",
                table: "tools",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "http_method",
                schema: "ai_gateway",
                table: "tools",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                schema: "ai_gateway",
                table: "mcp_server_defs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "openapi");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_type",
                schema: "ai_gateway",
                table: "mcp_server_defs");

            migrationBuilder.AlterColumn<string>(
                name: "http_path",
                schema: "ai_gateway",
                table: "tools",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "http_method",
                schema: "ai_gateway",
                table: "tools",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
