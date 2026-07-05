using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace McpGateway.IntegrationTests.McpUpstream;

public class McpUpstreamIntegrationTests : IClassFixture<McpApiFactory>, IClassFixture<UpstreamMcpFixture>
{
    private readonly McpApiFactory _factory;
    private readonly UpstreamMcpFixture _upstream;

    public McpUpstreamIntegrationTests(McpApiFactory factory, UpstreamMcpFixture upstream)
    {
        _factory = factory;
        _upstream = upstream;
    }

    private HttpClient CreateGatewayClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client;
    }

    private static HttpClient CreateAdminClient(McpApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Admin", "test-admin@corp.local");
        return client;
    }

    private static StringContent JsonBody(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadFirstEventData(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        string? line = await reader.ReadLineAsync();
        while (line is not null)
        {
            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..];
                return JsonDocument.Parse(json).RootElement.Clone();
            }

            line = await reader.ReadLineAsync();
        }

        throw new InvalidOperationException("MCP response had no 'data:' line");
    }

    [Fact]
    public async Task Register_Approve_List_Call_ForwardsToUpstream()
    {
        var upstreamUrl = _upstream.Endpoint + "/mcp";
        using var admin = CreateAdminClient(_factory);

        var registerResponse = await admin.PostAsJsonAsync("/admin/servers", new
        {
            name = "echo-upstream",
            displayName = "Echo Upstream",
            sourceType = "mcp-upstream",
            specSourceUrl = upstreamUrl,
            upstreamUrl,
            baseUrl = upstreamUrl,
            authStrategy = "static",
            authConfig = new { apiKey = "test-key" },
            toolMode = "all",
            clientProfile = "universal",
            pollIntervalMinutes = 1440
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var approveResponse = await admin.PostAsync("/admin/servers/echo-upstream/approve", null);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var mcp = CreateGatewayClient();
        var listResponse = await mcp.PostAsync("/mcp/echo-upstream", JsonBody(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
            @params = new { }
        }));
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await ReadFirstEventData(listResponse);
        listBody.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Should().Contain(t => t.GetProperty("name").GetString() == "echo");

        var callResponse = await mcp.PostAsync("/mcp/echo-upstream", JsonBody(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new
            {
                name = "echo",
                arguments = new { msg = "pluto" }
            }
        }));
        callResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var callBody = await ReadFirstEventData(callResponse);
        var text = callBody.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().Contain("pluto");
    }
}
