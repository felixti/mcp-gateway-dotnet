using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.McpUpstream;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.SpecManagement;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Auth;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Management.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class ServerManagementServiceUpdateTests
{
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();
    private readonly ISpecVersionRepository _specVersionRepo = Substitute.For<ISpecVersionRepository>();
    private readonly IToolStore _toolStore = Substitute.For<IToolStore>();
    private readonly IToolGenerator _toolGenerator = Substitute.For<IToolGenerator>();
    private readonly ISpecFetcher _specFetcher = Substitute.For<ISpecFetcher>();
    private readonly IOpenApiSpecValidator _specValidator = Substitute.For<IOpenApiSpecValidator>();
    private readonly ISpecDiffService _diffService = Substitute.For<ISpecDiffService>();
    private readonly ICallerIdentityAccessor _caller = Substitute.For<ICallerIdentityAccessor>();
    private readonly IMcpUpstreamClient _upstreamClient = Substitute.For<IMcpUpstreamClient>();
    private readonly UpstreamCatalogImporter _catalogImporter = new();

    private ServerManagementService CreateSut()
    {
        _specValidator.Validate(Arg.Any<string>(), Arg.Any<ClientProfile>())
            .Returns(new SpecValidationReport([], []));
        return new(
            _serverRepo, _specVersionRepo, _toolStore, _toolGenerator, _specValidator, _specFetcher, _diffService, _caller,
            _upstreamClient, _catalogImporter, NullLogger<ServerManagementService>.Instance);
    }

    [Fact]
    public async Task UpdateAsync_ChangesDisplayName()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            DisplayName = "Old",
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            SpecContent = "{}",
            SpecHash = "h",
            Status = "active",
            ApprovalStatus = "pending"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var req = new UpdateServerRequest("New Name", null, null, null, null, null, null, null, null, null);
        var sut = CreateSut();
        var result = await sut.UpdateAsync("inv", req, CancellationToken.None);

        result.DisplayName.Should().Be("New Name");
        await _serverRepo.Received(1).UpdateAsync(Arg.Is<McpServerDefinition>(d => d.DisplayName == "New Name"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_DisablesServerAndRemovesFromStore()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            Status = "active",
            ApprovalStatus = "approved",
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        await sut.DeleteAsync("inv", CancellationToken.None);

        await _serverRepo.Received(1).UpdateAsync(
            Arg.Is<McpServerDefinition>(d => d.Status == "disabled"),
            Arg.Any<CancellationToken>());
        _toolStore.Received(1).RemoveServer("inv");
    }

    [Fact]
    public async Task RefreshAsync_SpecUnchanged_NoChange()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecSourceUrl = "https://x/openapi.json",
            SpecContent = "{}",
            SpecHash = ComputeHash("{}"),
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "approved"
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _specFetcher.FetchAsync(SpecSource.FromUrl("https://x/openapi.json"), Arg.Any<CancellationToken>())
            .Returns(new FetchedSpec("{}", ComputeHash("{}"), SpecFormat.Json));

        var sut = CreateSut();
        var result = await sut.RefreshAsync("inv", CancellationToken.None);

        result.SpecChanged.Should().BeFalse();
        result.ApprovalStatus.Should().Be("approved");
    }

    [Fact]
    public async Task RefreshAsync_SpecChanged_SetsChangesPendingAndRemovesFromStore()
    {
        var oldContent = """{"openapi":"3.0.0","info":{"title":"X","version":"1"},"paths":{"/a":{"get":{"responses":{"200":{"description":"OK"}}}}}}""";
        var newContent = """{"openapi":"3.0.0","info":{"title":"X","version":"2"},"paths":{"/a":{"get":{"responses":{"200":{"description":"OK"}}}}}}""";
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecSourceUrl = "https://x/openapi.json",
            SpecContent = oldContent,
            SpecHash = ComputeHash(oldContent),
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "approved"
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _specFetcher.FetchAsync(SpecSource.FromUrl("https://x/openapi.json"), Arg.Any<CancellationToken>())
            .Returns(new FetchedSpec(newContent, ComputeHash(newContent), SpecFormat.Json));
        _toolGenerator.Generate(Arg.Any<Microsoft.OpenApi.Models.OpenApiDocument>(), Arg.Any<ClientProfile>())
            .Returns(new List<GeneratedTool>());
        _diffService.Diff(oldContent, newContent, Arg.Any<ClientProfile>())
            .Returns(new SpecDiffResult([], [], []));

        var sut = CreateSut();
        var result = await sut.RefreshAsync("inv", CancellationToken.None);

        result.SpecChanged.Should().BeTrue();
        result.ApprovalStatus.Should().Be("changes_pending");
        _toolStore.Received(1).RemoveServer("inv");
        await _specVersionRepo.Received(1).AddAsync(Arg.Any<SpecVersion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAsync_LoadsToolsIntoStore()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "pending",
            ToolMode = ToolMode.All,
            Tools = new List<ToolDefinition>
            {
                new() { ToolName = "ping", Description = "ping", HttpMethod = "GET", HttpPath = "/ping", InputSchema = "{}", AuthConfig = "{}" }
            }
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _caller.GetAdminUpn().Returns("admin@corp.com");

        var sut = CreateSut();
        var result = await sut.ApproveAsync("inv", CancellationToken.None);

        result.ApprovalStatus.Should().Be("approved");
        result.ApprovedBy.Should().Be("admin@corp.com");
        _toolStore.Received(1).AddServer(Arg.Is<McpServerDefinition>(d => d.ApprovalStatus == "approved"));
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
