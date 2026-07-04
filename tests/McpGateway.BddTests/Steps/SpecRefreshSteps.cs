using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using McpGateway.BddTests.Support;
using McpGateway.Management.Contracts;
using McpGateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Reqnroll;

namespace McpGateway.BddTests.Steps;

[Binding]
public sealed class SpecRefreshSteps
{
    private readonly TestContext _context;

    public SpecRefreshSteps(TestContext context)
    {
        _context = context;
    }

    [Given(@"an OpenAPI spec with (\d+) operation named ""([^""]+)""")]
    public void GivenSpecWithNOperations(int count, string name)
    {
        _context.SpecContent = count switch
        {
            1 => OpenApiSpecs.SingleOperationNoRef,
            2 => OpenApiSpecs.TwoOperations,
            _ => throw new InvalidOperationException($"No fixture for {count} operations")
        };
        _context.ServerName = name;
    }

    [When(@"the admin uploads a new spec with an added endpoint to the server")]
    public async Task WhenAdminUploadsSpecWithAddedEndpoint()
    {
        _context.SpecContent = OpenApiSpecs.TwoOperationsWithExtraEndpoint;
        await UploadSpec();
    }

    [StepDefinition(@"the admin uploads a new spec with a changed tool description to the server")]
    public async Task WhenAdminUploadsSpecWithChangedDescription()
    {
        _context.SpecContent = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Updated API", "version": "1.0.0" },
          "paths": {
            "/metrics": { "get": { "operationId": "getMetrics", "summary": "Get metrics (updated description)", "responses": { "200": { "description": "OK" } } } }
          }
        }
        """;
        await UploadSpec();
    }

    [When(@"the admin uploads the same spec again to the server")]
    public async Task WhenAdminUploadsSameSpecAgain()
    {
        await UploadSpec();
    }

    [StepDefinition(@"the admin re-approves the server via POST \/admin\/servers\/\{name\}\/approve")]
    public async Task WhenAdminReApprovesServer()
    {
        var client = _context.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Admin", _context.CurrentToken!);
        var response = await client.PostAsync($"/admin/servers/{_context.ServerName}/approve", content: null);
        _context.LastResponseStatus = (int)response.StatusCode;
    }

    [Then(@"the server approval status is ""([^""]+)""")]
    public async Task ThenServerApprovalStatusIs(string expected)
    {
        await using var context = _context.Factory!.CreateDbContext();
        var server = await context.ServerDefinitions.AsNoTracking().FirstAsync(s => s.Name == _context.ServerName);
        server.ApprovalStatus.Should().Be(expected);
    }

    [Then(@"the server has (\d+) tools in the database")]
    public async Task ThenServerHasNToolsInDatabase(int expected)
    {
        await using var context = _context.Factory!.CreateDbContext();
        var server = await context.ServerDefinitions
            .Include(s => s.Tools)
            .AsNoTracking()
            .FirstAsync(s => s.Name == _context.ServerName);
        server.Tools.Should().HaveCount(expected);
    }

    [Then(@"the tool description in the database matches the new spec")]
    public async Task ThenToolDescriptionMatchesNewSpec()
    {
        await using var context = _context.Factory!.CreateDbContext();
        var server = await context.ServerDefinitions
            .Include(s => s.Tools)
            .AsNoTracking()
            .FirstAsync(s => s.Name == _context.ServerName);
        server.Tools.Should().Contain(t => t.ToolName == "getmetrics" && t.Description.Contains("updated description"));
    }

    private async Task UploadSpec()
    {
        var client = _context.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Admin", _context.CurrentToken!);
        var payload = new SpecUploadRequest(Content: _context.SpecContent!, ContentType: "application/json");
        var response = await client.PostAsJsonAsync($"/admin/servers/{_context.ServerName}/spec", payload);
        _context.LastResponseStatus = (int)response.StatusCode;
        _context.LastResponseBody = await response.Content.ReadAsStringAsync();
    }
}
