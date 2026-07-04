using System.Net.Http.Headers;
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
public sealed class AuthStrategiesSteps
{
    private readonly TestContext _context;

    public AuthStrategiesSteps(TestContext context)
    {
        _context = context;
    }

    [Given(@"the server uses auth_strategy ""([^""]+)"" with resource ""([^""]+)""")]
    public async Task GivenServerUsesOboWithResource(string strategy, string resource)
    {
        await UpdateServerAuthStrategy(strategy, new JsonObject { ["resource"] = resource });
    }

    [Given(@"the server uses auth_strategy ""([^""]+)""")]
    public async Task GivenServerUsesPassthrough(string strategy)
    {
        await UpdateServerAuthStrategy(strategy, new JsonObject());
    }

    [Given(@"the server uses auth_strategy ""static"" with api_key ""([^""]+)""")]
    public async Task GivenServerUsesStaticWithApiKey(string apiKey)
    {
        await UpdateServerAuthStrategy("static", new JsonObject { ["apiKey"] = apiKey });
    }

    [Then(@"the server has auth_strategy ""([^""]+)"" in the database")]
    public async Task ThenServerHasAuthStrategy(string expected)
    {
        await using var context = _context.Factory!.CreateDbContext();
        var server = await context.ServerDefinitions.AsNoTracking().FirstAsync(s => s.Name == _context.ServerName);
        server.AuthStrategy.Should().Be(expected);
    }

    [Then(@"the server auth_config contains ""([^""]+)"" in the database")]
    public async Task ThenServerAuthConfigContains(string fragment)
    {
        await using var context = _context.Factory!.CreateDbContext();
        var server = await context.ServerDefinitions.AsNoTracking().FirstAsync(s => s.Name == _context.ServerName);
        server.AuthConfig.Should().Contain(fragment);
    }

    private async Task UpdateServerAuthStrategy(string strategy, JsonObject authConfig)
    {
        var client = _context.Factory!.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Admin", _context.CurrentToken!);
        var body = new
        {
            DisplayName = _context.ServerName,
            AuthStrategy = strategy,
            AuthConfig = authConfig
        };
        var response = await client.PatchAsync($"/admin/servers/{_context.ServerName}",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        _context.LastResponseStatus = (int)response.StatusCode;
        _context.LastResponseBody = await response.Content.ReadAsStringAsync();
    }
}
