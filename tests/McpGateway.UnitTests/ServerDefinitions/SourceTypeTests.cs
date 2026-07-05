using McpGateway.Core.ServerDefinitions;

namespace McpGateway.UnitTests.ServerDefinitions;

public class SourceTypeTests
{
    [Fact]
    public void McpServerDefinition_defaults_to_openapi_source_type()
    {
        var def = new McpServerDefinition();
        Assert.Equal(SourceType.OpenApi, def.SourceType);
    }

    [Theory]
    [InlineData(SourceType.OpenApi, "openapi")]
    [InlineData(SourceType.McpUpstream, "mcp-upstream")]
    public void SourceType_maps_to_canonical_string(SourceType type, string expected)
    {
        Assert.Equal(expected, type.ToCanonicalString());
    }
}
