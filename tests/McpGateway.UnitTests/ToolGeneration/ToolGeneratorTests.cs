using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.ToolGeneration;

public class ToolGeneratorTests
{
    private readonly ToolGenerator _generator = new();

    // ponytail: deviation from plan — same case-preservation issue as ToolNameResolverTests.
    // ToolNameResolver lowercases operationId; test must follow the implementation.
    [Fact]
    public void Generate_FromMinimalSpec_ReturnsOneTool()
    {
        const string spec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Test API", "version": "1.0.0" },
          "paths": {
            "/users": {
              "get": {
                "operationId": "listUsers",
                "summary": "List users",
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

        var tools = _generator.Generate(spec, ClientProfile.Universal);

        tools.Should().ContainSingle();
        tools[0].Name.Should().Be("listusers");
        tools[0].Description.Should().Be("List users");
        tools[0].HttpMethod.Should().Be("GET");
        tools[0].HttpPath.Should().Be("/users");
    }

    [Fact]
    public void Generate_SynthesizesName_WhenOperationIdMissing()
    {
        const string spec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Test API", "version": "1.0.0" },
          "paths": {
            "/users/{id}": {
              "get": {
                "summary": "Get a user",
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

        var tools = _generator.Generate(spec, ClientProfile.Universal);

        tools.Should().ContainSingle();
        tools[0].Name.Should().Be("get_users_id");
    }

    [Fact]
    public void Generate_AppliesPaginationNote()
    {
        const string spec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Test API", "version": "1.0.0" },
          "paths": {
            "/users": {
              "get": {
                "operationId": "listUsers",
                "summary": "List users",
                "parameters": [
                  { "name": "limit", "in": "query", "schema": { "type": "integer" } },
                  { "name": "offset", "in": "query", "schema": { "type": "integer" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

        var tools = _generator.Generate(spec, ClientProfile.Universal);

        tools[0].Description.Should().Contain("Pagination");
    }
}
