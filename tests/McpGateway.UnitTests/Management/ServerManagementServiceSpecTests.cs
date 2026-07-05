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
using McpGateway.Management.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class ServerManagementServiceSpecTests
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
    public async Task UploadSpecAsync_UpdatesSpecAndMarksChangesPending()
    {
        var oldHash = ComputeHash("""{"openapi":"3.0.0","info":{"title":"X","version":"1"},"paths":{"/a":{"get":{"responses":{"200":{"description":"OK"}}}}}}""");
        var newContent = """{"openapi":"3.0.0","info":{"title":"X","version":"2"},"paths":{"/b":{"get":{"responses":{"200":{"description":"OK"}}}}}}""";
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = """{"openapi":"3.0.0","info":{"title":"X","version":"1"},"paths":{"/a":{"get":{"responses":{"200":{"description":"OK"}}}}}}""",
            SpecHash = oldHash,
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "approved"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _toolGenerator.Generate(Arg.Any<Microsoft.OpenApi.Models.OpenApiDocument>(), Arg.Any<ClientProfile>())
            .Returns(new List<GeneratedTool>());

        var sut = CreateSut();
        var result = await sut.UploadSpecAsync("inv", new SpecUploadRequest(newContent, "application/json"), CancellationToken.None);

        result.ApprovalStatus.Should().Be("changes_pending");
        _toolStore.Received(1).RemoveServer("inv");
    }

    [Fact]
    public async Task UpdateSpecSourceAsync_Persists()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        var result = await sut.UpdateSpecSourceAsync("inv", new SpecSourceUpdateRequest("https://new/openapi.json"), CancellationToken.None);

        result.SpecSourceUrl.Should().Be("https://new/openapi.json");
        await _serverRepo.Received(1).UpdateAsync(Arg.Is<McpServerDefinition>(d => d.SpecSourceUrl == "https://new/openapi.json"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSpecAsync_ReturnsContent()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = """{"foo":"bar"}""",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo"
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        var (content, hash) = await sut.GetSpecAsync("inv", CancellationToken.None);
        content.Should().Contain("foo");
        hash.Should().Be(ComputeHash("""{"foo":"bar"}"""));
    }

    [Fact]
    public async Task GetSpecDiffAsync_NotFound_Throws()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns((McpServerDefinition?)null);
        var sut = CreateSut();
        Func<Task> act = () => sut.GetSpecDiffAsync("inv", Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<McpGateway.Management.Exceptions.NotFoundException>();
    }

    [Fact]
    public async Task GetSpecDiffAsync_HistoricalVersionNotFound_Throws()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = """{"foo":"bar"}""",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo"
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _specVersionRepo.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((SpecVersion?)null);

        var sut = CreateSut();
        Func<Task> act = () => sut.GetSpecDiffAsync("inv", Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<McpGateway.Management.Exceptions.NotFoundException>();
    }

    [Fact]
    public async Task GetSpecDiffAsync_ReturnsDiffPayload()
    {
        var versionId = Guid.NewGuid();
        var oldContent = """{"old":true}""";
        var newContent = """{"new":true}""";
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            SpecContent = newContent,
            SpecHash = ComputeHash(newContent),
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ClientProfile = ClientProfile.Universal
        };
        var historical = new SpecVersion
        {
            Id = versionId,
            ServerDefinitionId = def.Id,
            SpecContent = oldContent,
            SpecHash = ComputeHash(oldContent)
        };
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(def);
        _specVersionRepo.GetAsync(versionId, Arg.Any<CancellationToken>()).Returns(historical);
        _diffService.Diff(oldContent, newContent, ClientProfile.Universal)
            .Returns(new SpecDiffResult(["added_tool"], [], []));

        var sut = CreateSut();
        var result = await sut.GetSpecDiffAsync("inv", versionId, CancellationToken.None);

        result.Should().NotBeNull();
        result.Added.Should().Contain("added_tool");
        result.ComparedHash.Should().Be(historical.SpecHash);
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
