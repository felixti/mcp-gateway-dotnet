using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.ToolGeneration;

public class SchemaTransformerTests
{
    private readonly SchemaTransformer _transformer = new();

    [Fact]
    public void Transform_Universal_InlinesRefs()
    {
        var schema = JsonNode.Parse("""
        {
          "$ref": "#/components/schemas/User"
        }
        """);
        var components = JsonNode.Parse("""
        {
          "schemas": {
            "User": { "type": "object", "properties": { "id": { "type": "integer" } } }
          }
        }
        """);

        var result = _transformer.Transform(schema!, ClientProfile.Universal, components);

        result["type"]!.GetValue<string>().Should().Be("object");
        result["properties"]!["id"]!["type"]!.GetValue<string>().Should().Be("integer");
    }

    [Fact]
    public void Transform_Universal_SplitsAnyOf()
    {
        var schema = JsonNode.Parse("""
        {
          "anyOf": [
            { "type": "string" },
            { "type": "integer" }
          ]
        }
        """);

        var result = _transformer.Transform(schema!, ClientProfile.Universal, null);

        result["oneOf"].Should().NotBeNull();
        result["oneOf"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void Transform_Cursor_TruncatesName()
    {
        var schema = JsonNode.Parse("""
        {
          "title": "ThisIsAVeryLongTitleThatShouldBeTruncatedBecauseCursorHasStrictLimitsOnToolNameLength",
          "type": "object"
        }
        """);

        var result = _transformer.Transform(schema!, ClientProfile.Cursor, null);

        result["title"]!.GetValue<string>().Should().HaveLength(60);
    }
}
