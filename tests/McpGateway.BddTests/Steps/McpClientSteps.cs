using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using McpGateway.BddTests.Support;
using Reqnroll;

namespace McpGateway.BddTests.Steps;

[Binding]
public sealed class McpClientSteps
{
    private readonly TestContext _context;

    public McpClientSteps(TestContext context)
    {
        _context = context;
    }

    [StepDefinition(@"an MCP client sends a tools\/list request")]
    public async Task WhenMcpClientListsTools()
    {
        var client = _context.Factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-token");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/mcp/{_context.ServerName}")
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}",
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await client.SendAsync(request);
        _context.LastResponseStatus = (int)response.StatusCode;
        _context.LastResponseBody = await response.Content.ReadAsStringAsync();
        _context.LastJsonResponse = McpResponseReader.Parse(response, _context.LastResponseBody);
    }

    [When(@"an MCP client sends a tools\/call for ""([^""]+)"" with arguments (.+)")]
    public async Task WhenMcpClientCallsToolWithArguments(string toolName, string argumentsJson)
    {
        var client = _context.Factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-token");
        var body = $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{{\"name\":\"{toolName}\",\"arguments\":{argumentsJson}}}}}";
        var request = new HttpRequestMessage(HttpMethod.Post, $"/mcp/{_context.ServerName}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await client.SendAsync(request);
        _context.LastResponseStatus = (int)response.StatusCode;
        _context.LastResponseBody = await response.Content.ReadAsStringAsync();
        _context.LastJsonResponse = McpResponseReader.Parse(response, _context.LastResponseBody);
    }

    [Then(@"the response status is (\d+)")]
    public void ThenResponseStatusIs(int status)
    {
        _context.LastResponseStatus.Should().Be(status);
    }

    [Then(@"the response is a tool result with isError true")]
    public void ThenResponseIsError()
    {
        _context.LastJsonResponse.Should().NotBeNull();
        var result = _context.LastJsonResponse!.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
    }

    [Then(@"the response content contains ""([^""]+)""")]
    public void ThenResponseContentContains(string fragment)
    {
        _context.LastJsonResponse.Should().NotBeNull();
        var result = _context.LastJsonResponse!.RootElement.GetProperty("result");
        var content = result.GetProperty("content")[0].GetProperty("text").GetString();
        content.Should().Contain(fragment);
    }
}
