using System.Net;
using FluentAssertions;
using McpGateway.Core.Proxy;

namespace McpGateway.UnitTests.Proxy;

public class ResponseWrapperTests
{
    private readonly ResponseWrapper _wrapper = new();

    [Fact]
    public async Task Wrap_Success_ReturnsContent()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"items\":[]}")
        };

        var result = await _wrapper.WrapAsync(response);

        result.IsError.Should().BeFalse();
        result.Content.Should().ContainSingle(c => c.Text == "{\"items\":[]}");
    }

    [Fact]
    public async Task Wrap_Error_ReturnsIsError()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid")
        };

        var result = await _wrapper.WrapAsync(response);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[HTTP 400]");
        result.Content[0].Text.Should().Contain("invalid");
    }

    [Fact]
    public async Task Wrap_LargeResponse_Truncated()
    {
        var large = new string('x', 11_000);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(large)
        };

        var result = await _wrapper.WrapAsync(response);

        result.Content[0].Text.Length.Should().BeLessThanOrEqualTo(10_240);
    }
}
