using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;
using Xunit;

namespace McpGateway.UnitTests.ToolGeneration;

public class OpenApiSpecValidatorTests
{
    private readonly OpenApiSpecValidator _sut = new();

    private const string ValidSpec = """
        {
          "openapi": "3.0.3",
          "info": { "title": "T", "version": "1.0.0" },
          "paths": {
            "/pets": {
              "get": {
                "operationId": "listPets",
                "summary": "List pets",
                "responses": {
                  "200": {
                    "description": "ok",
                    "content": {
                      "application/json": {
                        "schema": { "type": "array" }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void Validate_ValidSpec_ReturnsNoErrors()
    {
        var report = _sut.Validate(ValidSpec, ClientProfile.Universal);

        report.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyPaths_ReturnsEmptyPathsError()
    {
        var spec = """
            {
              "openapi": "3.0.3",
              "info": { "title": "T", "version": "1.0.0" },
              "paths": {}
            }
            """;

        var report = _sut.Validate(spec, ClientProfile.Universal);

        report.Errors.Should().ContainSingle(e => e.Code == "empty_paths");
    }

    [Fact]
    public void Validate_MissingOperationId_ReturnsWarning()
    {
        var report = _sut.Validate(ValidSpec.Replace("\"operationId\": \"listPets\",", ""), ClientProfile.Universal);

        report.Warnings.Should().ContainSingle(w => w.Code == "missing_operation_id");
    }

    [Fact]
    public void Validate_DuplicateToolNames_ReturnsError()
    {
        var spec = """
            {
              "openapi": "3.0.3",
              "info": { "title": "T", "version": "1.0.0" },
              "paths": {
                "/a": { "get": { "operationId": "same", "responses": { "200": { "description": "ok" } } } },
                "/b": { "get": { "operationId": "same", "responses": { "200": { "description": "ok" } } } }
              }
            }
            """;

        var report = _sut.Validate(spec, ClientProfile.Universal);

        report.Errors.Should().ContainSingle(e => e.Code == "duplicate_tool_name");
    }

    [Fact]
    public void Validate_MissingPathParameter_ReturnsError()
    {
        var spec = """
            {
              "openapi": "3.0.3",
              "info": { "title": "T", "version": "1.0.0" },
              "paths": {
                "/pets/{petId}": {
                  "get": {
                    "operationId": "getPet",
                    "responses": { "200": { "description": "ok" } }
                  }
                }
              }
            }
            """;

        var report = _sut.Validate(spec, ClientProfile.Universal);

        report.Errors.Should().ContainSingle(e => e.Code == "missing_path_parameter");
    }

    [Fact]
    public void Validate_UnsupportedRequestBody_ReturnsError()
    {
        var spec = """
            {
              "openapi": "3.0.3",
              "info": { "title": "T", "version": "1.0.0" },
              "paths": {
                "/pets": {
                  "post": {
                    "operationId": "createPet",
                    "requestBody": {
                      "content": {
                        "application/x-www-form-urlencoded": {
                          "schema": { "type": "object" }
                        }
                      }
                    },
                    "responses": { "201": { "description": "ok" } }
                  }
                }
              }
            }
            """;

        var report = _sut.Validate(spec, ClientProfile.Universal);

        report.Warnings.Should().ContainSingle(w => w.Code == "unsupported_request_body");
    }

    [Fact]
    public void Validate_NoJsonResponse_ReturnsWarning()
    {
        var spec = """
            {
              "openapi": "3.0.3",
              "info": { "title": "T", "version": "1.0.0" },
              "paths": {
                "/pets": {
                  "get": {
                    "operationId": "listPets",
                    "responses": { "200": { "description": "ok" } }
                  }
                }
              }
            }
            """;

        var report = _sut.Validate(spec, ClientProfile.Universal);

        report.Warnings.Should().ContainSingle(w => w.Code == "non_json_response_body");
    }

    [Fact]
    public void Validate_HeaderParameter_ReturnsWarning()
    {
        var spec = """
            {
              "openapi": "3.0.3",
              "info": { "title": "T", "version": "1.0.0" },
              "paths": {
                "/pets": {
                  "get": {
                    "operationId": "listPets",
                    "parameters": [
                      { "name": "X-Api-Key", "in": "header", "schema": { "type": "string" } }
                    ],
                    "responses": { "200": { "description": "ok" } }
                  }
                }
              }
            }
            """;

        var report = _sut.Validate(spec, ClientProfile.Universal);

        report.Warnings.Should().ContainSingle(w => w.Code == "ignored_header_parameter");
    }

    [Fact]
    public void Validate_RecursiveReference_ReturnsError()
    {
        var spec = """
            {
              "openapi": "3.0.3",
              "info": { "title": "T", "version": "1.0.0" },
              "paths": {
                "/pets": {
                  "get": {
                    "operationId": "listPets",
                    "responses": {
                      "200": {
                        "description": "ok",
                        "content": {
                          "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Pet": {
                    "type": "object",
                    "properties": {
                      "friend": { "$ref": "#/components/schemas/Pet" }
                    }
                  }
                }
              }
            }
            """;

        var report = _sut.Validate(spec, ClientProfile.Universal);

        report.Errors.Should().ContainSingle(e => e.Code == "recursive_reference");
    }

    [Fact]
    public void Validate_UnresolvedReference_ReturnsError()
    {
        var spec = """
            {
              "openapi": "3.0.3",
              "info": { "title": "T", "version": "1.0.0" },
              "paths": {
                "/pets": {
                  "get": {
                    "operationId": "listPets",
                    "responses": {
                      "200": {
                        "description": "ok",
                        "content": {
                          "application/json": { "schema": { "$ref": "#/components/schemas/Missing" } }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        var report = _sut.Validate(spec, ClientProfile.Universal);

        report.Errors.Should().ContainSingle(e => e.Code == "unresolved_reference");
    }
}
