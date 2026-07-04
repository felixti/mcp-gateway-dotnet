using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace McpGateway.IntegrationTests;

[Collection("AdminApi")]
public class AdminApiTests : IClassFixture<AdminApiFactory>
{
    private const string DevAdminHeader = "X-Dev-Admin";
    private const string AdminUpn = "alice@corp.local";

    private readonly AdminApiFactory _factory;

    public AdminApiTests(AdminApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAdminHeader, AdminUpn);
        return client;
    }

    private static StringContent JsonBody(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static readonly string SampleSpec = """
    {
      "openapi": "3.0.0",
      "info": { "title": "Invoice API", "version": "1.0.0" },
      "paths": {
        "/invoices": {
          "get": {
            "operationId": "listInvoices",
            "summary": "List invoices",
            "responses": { "200": { "description": "OK" } }
          }
        }
      }
    }
    """;

    [Fact]
    public async Task Register_Approve_AndList_ProducesApprovedServerInToolStore()
    {
        var client = CreateAdminClient();

        var create = new CreateServerRequest(
            "invoice-api", "Invoice API", "desc",
            null, SampleSpec,
            "https://invoice.example.com", "obo",
            new JsonObject { ["resource"] = "api://invoice-api/.default" },
            "all", "universal", 1440, AdminUpn);

        var post = await client.PostAsync("/admin/servers", JsonBody(create));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IToolStore>();
            store.Contains("invoice-api").Should().BeFalse();
        }

        var approve = await client.PostAsync("/admin/servers/invoice-api/approve", content: null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approve.Content.ReadFromJsonAsync<ApproveResponse>();
        approved!.ApprovalStatus.Should().Be("approved");
        approved.ApprovedBy.Should().Be(AdminUpn);

        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IToolStore>();
            store.Contains("invoice-api").Should().BeTrue();
        }

        var list = await client.GetAsync("/admin/servers");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var servers = await list.Content.ReadFromJsonAsync<List<ServerResponse>>();
        servers!.Select(s => s.Name).Should().Contain("invoice-api");
    }

    [Fact]
    public async Task PatchServer_UpdatesDisplayName()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "patch-test");

        var patch = new UpdateServerRequest("Patched Name", null, null, null, null, null, null, null, null, null);
        var resp = await client.PatchAsync("/admin/servers/patch-test", JsonBody(patch));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ServerResponse>();
        body!.DisplayName.Should().Be("Patched Name");
    }

    [Fact]
    public async Task DeleteServer_SetsDisabledStatusAndRemovesFromStore()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "del-test");

        var resp = await client.DeleteAsync("/admin/servers/del-test");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IToolStore>();
        store.Contains("del-test").Should().BeFalse();
    }

    [Fact]
    public async Task RefreshServer_WhenSpecChanged_SetsChangesPending()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "refresh-test");

        var newSpec = SampleSpec.Replace("listInvoices", "listInvoicesV2");
        var upload = new SpecUploadRequest(newSpec, "application/json");
        var resp = await client.PostAsync("/admin/servers/refresh-test/spec", JsonBody(upload));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ServerResponse>();
        body!.ApprovalStatus.Should().Be("changes_pending");
    }

    [Fact]
    public async Task ListTools_ReturnsEffectiveDescription()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "tools-test");

        var ovr = new PutOverrideRequest("Hardened description (admin review)");
        var put = await client.PutAsync("/admin/servers/tools-test/tools/listinvoices/override", JsonBody(ovr));
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetAsync("/admin/servers/tools-test/tools");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var tools = await list.Content.ReadFromJsonAsync<List<ToolResponse>>();
        tools!.Should().ContainSingle(t => t.EffectiveDescription == "Hardened description (admin review)" && t.HasOverride);
    }

    [Fact]
    public async Task UpdateTool_HidesToolViaVisibilityToggle()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "visibility-test");

        var body = new UpdateToolRequest(null, false);
        var resp = await client.PatchAsync("/admin/servers/visibility-test/tools/listinvoices", JsonBody(body));
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetAsync("/admin/servers/visibility-test/tools");
        var tools = await list.Content.ReadFromJsonAsync<List<ToolResponse>>();
        tools!.Single().Visible.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteOverride_RevertsToSpecDescription()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "revert-test");
        await client.PutAsync("/admin/servers/revert-test/tools/listinvoices/override",
            JsonBody(new PutOverrideRequest("Custom")));

        var resp = await client.DeleteAsync("/admin/servers/revert-test/tools/listinvoices/override");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetAsync("/admin/servers/revert-test/tools");
        var tools = await list.Content.ReadFromJsonAsync<List<ToolResponse>>();
        tools!.Single().HasOverride.Should().BeFalse();
    }

    [Fact]
    public async Task IssueApiKey_ReturnsFullKeyOnlyOnce()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "apikey-test");

        var resp = await client.PostAsync("/admin/servers/apikey-test/api-keys",
            JsonBody(new CreateApiKeyRequest("ci-runner", new[] { "apikey-test" })));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        body!.FullKey.Should().StartWith("mgk_");
        body.KeyPrefix.Should().HaveLength(12);

        var list = await client.GetAsync("/admin/servers/apikey-test/api-keys");
        var keys = await list.Content.ReadFromJsonAsync<List<ApiKeySummary>>();
        keys!.Single().KeyPrefix.Should().Be(body.KeyPrefix);
    }

    [Fact]
    public async Task RevokeApiKey_Returns204()
    {
        var client = CreateAdminClient();
        await RegisterAndApproveServer(client, "revoke-test");
        var issued = await (await client.PostAsync("/admin/servers/revoke-test/api-keys",
            JsonBody(new CreateApiKeyRequest("k", new[] { "revoke-test" }))))
            .Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        var resp = await client.DeleteAsync($"/admin/servers/revoke-test/api-keys/{issued!.Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await client.GetAsync("/admin/servers/revoke-test/api-keys");
        var keys = await list.Content.ReadFromJsonAsync<List<ApiKeySummary>>();
        keys!.Single().RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadSpec_AndGetSpec_RoundTrips()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "spec-roundtrip");

        var newSpec = SampleSpec.Replace("1.0.0", "2.0.0");
        var upload = new SpecUploadRequest(newSpec, "application/json");
        var uploadResp = await client.PostAsync("/admin/servers/spec-roundtrip/spec", JsonBody(upload));
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResp = await client.GetAsync("/admin/servers/spec-roundtrip/spec");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("content").GetString().Should().Contain("2.0.0");
    }

    [Fact]
    public async Task UpdateSpecSource_PersistsUrl()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "source-test");

        var body = new SpecSourceUpdateRequest("https://new.example.com/openapi.json");
        var resp = await client.PutAsync("/admin/servers/source-test/spec-source", JsonBody(body));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var server = await resp.Content.ReadFromJsonAsync<ServerResponse>();
        server!.SpecSourceUrl.Should().Be("https://new.example.com/openapi.json");
    }

    [Fact]
    public async Task RegisterServer_DuplicateName_Returns409()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "dup-name");

        var resp = await client.PostAsync("/admin/servers", JsonBody(BuildCreate("dup-name")));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RegisterServer_InvalidPayload_Returns400()
    {
        var client = CreateAdminClient();
        var bad = new CreateServerRequest(
            "Bad Name With Spaces", "X", null, null, SampleSpec,
            "https://x", "magic", new JsonObject(), "all", "universal", 1440, null);

        var resp = await client.PostAsync("/admin/servers", JsonBody(bad));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnauthenticatedCall_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/admin/servers");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSpecDiff_UnknownVersion_Returns404()
    {
        var client = CreateAdminClient();
        await RegisterServer(client, "diff-test");
        var resp = await client.GetAsync($"/admin/servers/diff-test/spec/diff/{Guid.NewGuid()}");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static CreateServerRequest BuildCreate(string name) => new(
        name, name, null, null, SampleSpec,
        "https://" + name + ".example.com", "obo",
        new JsonObject { ["resource"] = "api://" + name + "/.default" },
        "all", "universal", 1440, AdminUpn);

    private async Task RegisterServer(HttpClient client, string name)
    {
        var resp = await client.PostAsync("/admin/servers", JsonBody(BuildCreate(name)));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task RegisterAndApproveServer(HttpClient client, string name)
    {
        await RegisterServer(client, name);
        var approve = await client.PostAsync($"/admin/servers/{name}/approve", content: null);
        approve.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
