using System.Text.Json;
using FluentAssertions;
using McpGateway.BddTests.Support;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;
using McpGateway.Persistence;
using Microsoft.EntityFrameworkCore;
using Reqnroll;

namespace McpGateway.BddTests.Steps;

[Binding]
public sealed class ClientProfilesSteps
{
    private readonly TestContext _context;

    public ClientProfilesSteps(TestContext context)
    {
        _context = context;
    }

    [Given(@"^an OpenAPI spec with a \$ref and an anyOf response named ""([^""]+)""$")]
    public void GivenSpecWithRefAndAnyOf(string name)
    {
        _context.SpecContent = OpenApiSpecs.SingleOperationWithRefAndAnyOf;
        _context.ServerName = name;
    }

    [Given(@"an OpenAPI spec with an anyOf response named ""([^""]+)""")]
    public void GivenSpecWithAnyOf(string name)
    {
        _context.SpecContent = OpenApiSpecs.OperationWithAnyOf;
        _context.ServerName = name;
    }

    [Given(@"an OpenAPI spec with a long schema title named ""([^""]+)""")]
    public void GivenSpecWithLongTitle(string name)
    {
        _context.SpecContent = OpenApiSpecs.OperationWithLongTitle;
        _context.ServerName = name;
    }

    [Given(@"the server uses client_profile ""([^""]+)""")]
    public async Task GivenServerUsesClientProfile(string profile)
    {
        var clientProfile = Enum.Parse<McpGateway.Core.ServerDefinitions.ClientProfile>(Capitalize(profile));
        var parser = new OpenApiParser();
        var document = parser.Parse(_context.SpecContent!);
        var generator = new ToolGenerator();
        var generated = generator.Generate(document, clientProfile);

        await using var context = _context.Factory!.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM ai_gateway.tools WHERE server_definition_id = (SELECT id FROM ai_gateway.mcp_server_defs WHERE name = {0})",
            _context.ServerName!);

        var server = await context.ServerDefinitions
            .FirstAsync(s => s.Name == _context.ServerName);
        server.ClientProfile = clientProfile;
        server.UpdatedAt = DateTime.UtcNow;
        foreach (var t in generated)
        {
            context.Tools.Add(new McpGateway.Persistence.Entities.ToolEntity
            {
                Id = Guid.NewGuid(),
                ToolName = t.Name,
                Description = t.Description,
                HttpMethod = t.HttpMethod,
                HttpPath = t.HttpPath,
                InputSchema = t.InputSchema?.ToJsonString() ?? "{}",
                OutputSchema = t.OutputSchema?.ToJsonString(),
                AuthConfig = "{}",
                Visible = true,
                ServerDefinitionId = server.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();
    }

    [Then(@"^the tool input schema does not contain ""\$ref""$")]
    public void ThenToolInputSchemaHasNoRef()
    {
        _context.LastJsonResponse.Should().NotBeNull();
        var tool = _context.LastJsonResponse!.RootElement.GetProperty("result").GetProperty("tools")[0];
        var inputSchema = tool.GetProperty("inputSchema").ToString();
        inputSchema.Should().NotContain("\"$ref\"");
    }

    [Then(@"the tool output schema contains a ""([^""]+)"" key")]
    public void ThenToolOutputSchemaContainsKey(string key)
    {
        _context.LastJsonResponse.Should().NotBeNull();
        var tool = _context.LastJsonResponse!.RootElement.GetProperty("result").GetProperty("tools")[0];
        var toolJson = tool.ToString();
        toolJson.Should().Contain($"\"{key}\"");
    }

    [Then(@"the tool output schema contains an ""anyOf"" key")]
    public void ThenToolOutputSchemaContainsAnyOf()
    {
        _context.LastJsonResponse.Should().NotBeNull();
        var tool = _context.LastJsonResponse!.RootElement.GetProperty("result").GetProperty("tools")[0];
        tool.ToString().Should().Contain("\"anyOf\"");
    }

    [Then(@"every schema ""title"" value is at most (\d+) characters long")]
    public void ThenEverySchemaTitleIsAtMostNChars(int max)
    {
        _context.LastJsonResponse.Should().NotBeNull();
        var tool = _context.LastJsonResponse!.RootElement.GetProperty("result").GetProperty("tools")[0];
        var toolJson = tool.ToString();
        using var doc = JsonDocument.Parse(toolJson);
        var titles = ExtractStringProperties(doc.RootElement, "title");
        titles.Should().OnlyContain(t => t.Length <= max);
    }

    private static IEnumerable<string> ExtractStringProperties(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == propertyName && property.Value.ValueKind == JsonValueKind.String)
                {
                    yield return property.Value.GetString()!;
                }
                foreach (var nested in ExtractStringProperties(property.Value, propertyName))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in ExtractStringProperties(item, propertyName))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value[1..];
}
