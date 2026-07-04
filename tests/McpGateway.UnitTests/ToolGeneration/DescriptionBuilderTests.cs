using FluentAssertions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.ToolGeneration;

public class DescriptionBuilderTests
{
    private readonly DescriptionBuilder _builder = new();

    [Fact]
    public void Build_PrefersSummary()
    {
        var description = _builder.Build("summary text", "description text", null);
        description.Should().Be("summary text");
    }

    [Fact]
    public void Build_FallsBackToDescription()
    {
        var description = _builder.Build(null, "description text", null);
        description.Should().Be("description text");
    }

    [Fact]
    public void Build_FallsBackToSynthesized()
    {
        var description = _builder.Build(null, null, "GET /users/{id}");
        description.Should().Be("GET /users/{id}");
    }

    [Fact]
    public void Build_TrimsAndDeduplicatesWhitespace()
    {
        var description = _builder.Build("  summary   text  ", null, null);
        description.Should().Be("summary text");
    }
}
