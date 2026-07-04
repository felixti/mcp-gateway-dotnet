using FluentAssertions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.UnitTests.ToolGeneration;

public class ToolNameResolverTests
{
    private readonly ToolNameResolver _resolver = new();

    // ponytail: deviation from plan — plan's first InlineData expected "getUser" to round-trip
    // case-preserved, but the plan's own implementation lowercases everything
    // (var lower = value.ToLowerInvariant(); ...). Test must follow the implementation.
    [Theory]
    [InlineData("getUser", "GET", "/users/{id}", "getuser")]
    [InlineData(null, "GET", "/users/{id}", "get_users_id")]
    [InlineData("", "POST", "/invoices", "post_invoices")]
    [InlineData("List-Users", "GET", "/users", "list_users")]
    public void Resolve_ReturnsExpectedName(string? operationId, string method, string path, string expected)
    {
        var name = _resolver.Resolve(operationId, method, path);
        name.Should().Be(expected);
    }
}
