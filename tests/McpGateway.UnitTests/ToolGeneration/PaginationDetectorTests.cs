using FluentAssertions;
using McpGateway.Core.ToolGeneration;
using Microsoft.OpenApi.Models;

namespace McpGateway.UnitTests.ToolGeneration;

public class PaginationDetectorTests
{
    private readonly PaginationDetector _detector = new();

    [Fact]
    public void Detect_WithLimitAndOffset_ReturnsPaginationNote()
    {
        var operation = new OpenApiOperation
        {
            Parameters = new List<OpenApiParameter>
            {
                new() { Name = "limit", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "integer" } },
                new() { Name = "offset", In = ParameterLocation.Query, Schema = new OpenApiSchema { Type = "integer" } }
            }
        };

        var note = _detector.Detect(operation);

        note.Should().Contain("limit");
        note.Should().Contain("offset");
    }

    [Fact]
    public void Detect_WithoutPaginationParams_ReturnsNull()
    {
        var operation = new OpenApiOperation
        {
            Parameters = new List<OpenApiParameter>
            {
                new() { Name = "id", In = ParameterLocation.Path, Schema = new OpenApiSchema { Type = "string" } }
            }
        };

        var note = _detector.Detect(operation);

        note.Should().BeNull();
    }
}
