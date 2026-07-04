using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.BddTests.Support;
using McpGateway.Management.Contracts;
using McpGateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Reqnroll;

namespace McpGateway.BddTests.Steps;

[Binding]
public sealed class ToolGenerationSteps
{
    private readonly TestContext _context;

    public ToolGenerationSteps(TestContext context)
    {
        _context = context;
    }

    [Given(@"a valid admin request context")]
    public void GivenValidAdminContext()
    {
        _context.CurrentToken = "alice@corp.local";
    }

    [Given(@"the upstream API stub responds with status 200")]
    public void GivenUpstreamApiStub()
    {
        _context.UpstreamApi!
            .Given(WireMock.RequestBuilders.Request.Create().UsingAnyMethod())
            .RespondWith(WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"ok\":true}"));
    }

    [Given(@"an OpenAPI spec with (\d+) operations named ""([^""]+)""")]
    public void GivenSpecWithCountOperations(int count, string name)
    {
        _context.SpecContent = count switch
        {
            12 => OpenApiSpecs.TwelveOperations,
            3 => OpenApiSpecs.ThreeOperations,
            2 => OpenApiSpecs.TwoOperations,
            1 => OpenApiSpecs.SingleOperationNoRef,
            _ => throw new InvalidOperationException($"No fixture for {count} operations")
        };
        _context.ServerName = name;
    }

    [Given(@"an OpenAPI spec with operation ""([^""]+)"" without operationId named ""([^""]+)""")]
    public void GivenSpecWithSynthesizedNameOperation(string _, string name)
    {
        _context.SpecContent = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Synth API", "version": "1.0.0" },
          "paths": {
            "/users/{id}": { "get": { "summary": "Get user", "parameters": [ { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } } ], "responses": { "200": { "description": "OK" } } } }
          }
        }
        """;
        _context.ServerName = name;
    }

    [StepDefinition(@"the admin registers the server via POST \/admin\/servers")]
    public async Task WhenAdminRegistersServer()
    {
        var client = _context.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Admin", _context.CurrentToken!);

        var payload = new CreateServerRequest(
            Name: _context.ServerName!,
            DisplayName: _context.ServerName!,
            Description: null,
            SpecSourceUrl: null,
            SpecContent: _context.SpecContent,
            BaseUrl: _context.UpstreamApi!.Urls[0],
            AuthStrategy: "static",
            AuthConfig: new JsonObject { ["apiKey"] = "test-key" },
            ToolMode: "all",
            ClientProfile: "universal",
            PollIntervalMinutes: 1440,
            CreatedBy: "alice@corp.local");

        var response = await client.PostAsJsonAsync("/admin/servers", payload);
        _context.LastResponseStatus = (int)response.StatusCode;
        _context.LastResponseBody = await response.Content.ReadAsStringAsync();
    }

    [StepDefinition(@"the admin approves the server via POST \/admin\/servers\/\{name\}\/approve")]
    public async Task WhenAdminApprovesServer()
    {
        var client = _context.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Admin", _context.CurrentToken!);
        var response = await client.PostAsync($"/admin/servers/{_context.ServerName}/approve", content: null);
        _context.LastResponseStatus = (int)response.StatusCode;
        _context.LastResponseBody = await response.Content.ReadAsStringAsync();
    }

    [When(@"an MCP client sends a tools\/call for ""([^""]+)"" without admin approval")]
    public async Task WhenMcpClientCallsToolWithoutApproval(string toolName)
    {
        var client = _context.Factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-mcp-token");
        var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + toolName + "\",\"arguments\":{}}}";
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

    [Then(@"(\d+) MCP tools are stored for the server")]
    public async Task ThenToolsAreStored(int expected)
    {
        await using var context = _context.Factory!.CreateDbContext();
        var server = await context.ServerDefinitions
            .Include(s => s.Tools)
            .AsNoTracking()
            .FirstAsync(s => s.Name == _context.ServerName);
        server.Tools.Should().HaveCount(expected);
    }

    [Then(@"each tool has a name, description, and input schema")]
    public async Task ThenEachToolHasNameDescriptionSchema()
    {
        await using var context = _context.Factory!.CreateDbContext();
        var server = await context.ServerDefinitions
            .Include(s => s.Tools)
            .AsNoTracking()
            .FirstAsync(s => s.Name == _context.ServerName);
        server.Tools.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.ToolName));
        server.Tools.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Description));
        server.Tools.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.InputSchema));
    }

    [Then(@"the server has a tool named ""([^""]+)""")]
    public async Task ThenServerHasToolNamed(string toolName)
    {
        await using var context = _context.Factory!.CreateDbContext();
        var server = await context.ServerDefinitions
            .Include(s => s.Tools)
            .AsNoTracking()
            .FirstAsync(s => s.Name == _context.ServerName);
        server.Tools.Should().Contain(t => t.ToolName == toolName);
    }

    [Then(@"the response contains (\d+) tools")]
    public void ThenResponseContainsTools(int expected)
    {
        _context.LastJsonResponse.Should().NotBeNull();
        var root = _context.LastJsonResponse!.RootElement;
        var tools = root.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().Be(expected);
    }

    [Then(@"the response is a JSON-RPC error with code (-?\d+)")]
    public void ThenResponseIsJsonRpcError(int code)
    {
        _context.LastJsonResponse.Should().NotBeNull();
        var root = _context.LastJsonResponse!.RootElement;
        var result = root.GetProperty("result");
        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().Contain(code.ToString());
    }
}
