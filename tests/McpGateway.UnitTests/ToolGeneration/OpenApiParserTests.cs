using FluentAssertions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.ToolGeneration;

public class OpenApiParserTests
{
    private const string MinimalOpenApiJson = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Test API", "version": "1.0.0" },
      "paths": {
        "/users": {
          "get": {
            "operationId": "listUsers",
            "summary": "List users",
            "responses": {
              "200": { "description": "OK" }
            }
          }
        }
      }
    }
    """;

    [Fact]
    public void ParseJson_ReturnsDocument_WithOnePath()
    {
        var parser = new OpenApiParser();
        var document = parser.Parse(MinimalOpenApiJson);

        document.Should().NotBeNull();
        document.Paths.Should().ContainSingle();
        document.Paths.First().Key.Should().Be("/users");
    }

    [Fact]
    public void ParseYaml_ReturnsDocument_WithOnePath()
    {
        // ponytail: deviation from plan — plan's test expected NotImplementedException,
        // but the plan's own implementation delegates both methods to the same reader.
        // The plan is internally inconsistent; this test follows the implementation note
        // ("OpenApiStringReader auto-detects JSON vs YAML, keep separate methods for API clarity").
        const string yaml = """
        openapi: 3.0.0
        info:
          title: Test API
          version: 1.0.0
        paths:
          /users:
            get:
              operationId: listUsers
              summary: List users
              responses:
                '200':
                  description: OK
        """;
        var parser = new OpenApiParser();
        var document = parser.ParseYaml(yaml);

        document.Should().NotBeNull();
        document.Paths.Should().ContainSingle();
        document.Paths.First().Key.Should().Be("/users");
    }
}
