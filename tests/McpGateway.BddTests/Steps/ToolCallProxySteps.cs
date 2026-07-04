using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using McpGateway.BddTests.Support;
using Reqnroll;
using WireMock.RequestBuilders;

namespace McpGateway.BddTests.Steps;

[Binding]
public sealed class ToolCallProxySteps
{
    private readonly TestContext _context;

    public ToolCallProxySteps(TestContext context)
    {
        _context = context;
    }

    [Given(@"^the upstream API stub responds with status (\d+) and body ""(.+)""$")]
    public void GivenUpstreamRespondsWithStatusAndBody(int status, string body)
    {
        _context.UpstreamApi!
            .Given(Request.Create().UsingAnyMethod())
            .RespondWith(WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(status)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));
    }

    [Given(@"the upstream API stub responds with status 200 and a (\d+)KB body")]
    public void GivenUpstreamRespondsWithLargeBody(int sizeKb)
    {
        var body = new string('x', sizeKb * 1024);
        _context.UpstreamApi!
            .Given(Request.Create().UsingAnyMethod())
            .RespondWith(WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(200)
                .WithBody(body));
    }

    [Given(@"the upstream API stub captures the request body")]
    public void GivenUpstreamCapturesRequestBody()
    {
        _context.UpstreamApi!
            .Given(Request.Create().UsingAnyMethod())
            .RespondWith(WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(200)
                .WithBody(req => req.Body ?? string.Empty));
    }

    [Then(@"the response body contains ""([^""]+)""")]
    public void ThenResponseBodyContains(string fragment)
    {
        _context.LastResponseBody.Should().Contain(fragment);
    }

    [Then(@"the upstream API received a GET request to ""([^""]+)""")]
    public void ThenUpstreamReceivedGetTo(string path)
    {
        var requests = _context.UpstreamApi!.LogEntries;
        requests.Should().Contain(r => r.RequestMessage.Method == "GET" && r.RequestMessage.AbsoluteUrl.Contains(path));
    }

    [Then(@"the upstream API received a GET request with query ""([^""]+)""")]
    public void ThenUpstreamReceivedGetWithQuery(string queryFragment)
    {
        var requests = _context.UpstreamApi!.LogEntries;
        requests.Should().Contain(r =>
            r.RequestMessage.Method == "GET" &&
            r.RequestMessage.AbsoluteUrl.Contains(queryFragment));
    }

    [Then(@"the upstream API received a JSON body with ""([^""]+)"" equal to (.+)")]
    public void ThenUpstreamReceivedJsonBody(string key, string value)
    {
        var requests = _context.UpstreamApi!.LogEntries;
        var body = requests.Select(r => r.RequestMessage.Body).FirstOrDefault(b => !string.IsNullOrEmpty(b));
        body.Should().NotBeNull();
        using var doc = JsonDocument.Parse(body!);
        var element = doc.RootElement.GetProperty(key);
        if (int.TryParse(value, out var intValue))
        {
            element.GetInt32().Should().Be(intValue);
        }
        else
        {
            element.GetString().Should().Be(value.Trim('"'));
        }
    }

    [Then(@"the response content length is at most (\d+) characters")]
    public void ThenResponseContentLengthIsAtMost(int max)
    {
        _context.LastJsonResponse.Should().NotBeNull();
        var result = _context.LastJsonResponse!.RootElement.GetProperty("result");
        var content = result.GetProperty("content")[0].GetProperty("text").GetString();
        content!.Length.Should().BeLessThanOrEqualTo(max);
    }
}
