using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.McpUpstream;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.UnitTests.McpUpstream;

public class UpstreamCatalogImporterTests
{
    [Fact]
    public void Import_Maps_Upstream_Tool_To_ToolDefinition()
    {
        var serverId = Guid.NewGuid();
        var importer = new UpstreamCatalogImporter();
        var upstreamTools = new List<UpstreamTool>
        {
            new(
                Name: "get_user",
                Description: "Returns a user by id",
                InputSchema: JsonNode.Parse("{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}}")!)
        };

        var result = importer.Import(upstreamTools, serverId);

        result.Should().ContainSingle();
        var tool = result[0];

        tool.ServerDefinitionId.Should().Be(serverId);
        tool.ToolName.Should().Be("get_user");
        tool.Description.Should().Be("Returns a user by id");
        tool.InputSchema.Should().Be("{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}}");
        tool.HttpMethod.Should().BeNull();
        tool.HttpPath.Should().BeNull();
        tool.Visible.Should().BeTrue();
        tool.AuthConfig.Should().Be("{}");
    }

    [Fact]
    public void Import_Null_InputSchema_Maps_To_Empty_Object()
    {
        var serverId = Guid.NewGuid();
        var importer = new UpstreamCatalogImporter();
        var upstreamTools = new List<UpstreamTool>
        {
            new(Name: "no_schema", Description: "No schema", InputSchema: null)
        };

        var result = importer.Import(upstreamTools, serverId);

        result.Should().ContainSingle();
        result[0].InputSchema.Should().Be("{}");
    }
}
