using FluentAssertions;
using McpGateway.Core.McpUpstream;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.UnitTests.McpUpstream;

public class UpstreamCatalogImporterTests
{
    [Fact]
    public void Import_Maps_Upstream_Tool_To_ToolDefinition()
    {
        var server = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "upstream-server",
            DisplayName = "Upstream Server"
        };

        var importer = new UpstreamCatalogImporter();
        var upstreamTools = new List<UpstreamTool>
        {
            new(
                Name: "get_user",
                Description: "Returns a user by id",
                InputSchema: "{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}}")
        };

        var result = importer.Import(server, upstreamTools);

        result.Should().ContainSingle();
        var tool = result[0];

        tool.ServerDefinitionId.Should().Be(server.Id);
        tool.ServerDefinition.Should().Be(server);
        tool.ToolName.Should().Be("get_user");
        tool.Description.Should().Be("Returns a user by id");
        tool.InputSchema.Should().Be("{\"type\":\"object\",\"properties\":{\"id\":{\"type\":\"string\"}}}");
        tool.HttpMethod.Should().BeNull();
        tool.HttpPath.Should().BeNull();
        tool.Visible.Should().BeTrue();
        tool.AuthConfig.Should().Be("{}");
        server.SourceType.Should().Be(SourceType.McpUpstream);
    }
}
