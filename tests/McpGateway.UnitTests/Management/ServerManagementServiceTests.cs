using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
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

public class ServerManagementServiceTests
{
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();
    private readonly ISpecVersionRepository _specVersionRepo = Substitute.For<ISpecVersionRepository>();
    private readonly IToolStore _toolStore = Substitute.For<IToolStore>();
    private readonly IToolGenerator _toolGenerator = Substitute.For<IToolGenerator>();
    private readonly ISpecFetcher _specFetcher = Substitute.For<ISpecFetcher>();
    private readonly ISpecDiffService _diffService = Substitute.For<ISpecDiffService>();
    private readonly ICallerIdentityAccessor _caller = Substitute.For<ICallerIdentityAccessor>();

    private ServerManagementService CreateSut() => new(
        _serverRepo, _specVersionRepo, _toolStore, _toolGenerator, _specFetcher, _diffService, _caller,
        NullLogger<ServerManagementService>.Instance);

    [Fact]
    public async Task RegisterAsync_WithSpecSource_FetchesAndGeneratesTools()
    {
        var specJson = """{"openapi":"3.0.0","info":{"title":"X","version":"1"},"paths":{}}""";
        _specFetcher.FetchAsync(SpecSource.FromUrl("https://x/openapi.json"), Arg.Any<CancellationToken>())
            .Returns(new FetchedSpec(specJson, ComputeHash(specJson), SpecFormat.Json));

        var generated = new List<GeneratedTool>
        {
            new() { Name = "ping", Description = "ping api", HttpMethod = "GET", HttpPath = "/ping" }
        };
        _toolGenerator.Generate(Arg.Any<Microsoft.OpenApi.Models.OpenApiDocument>(), Arg.Any<ClientProfile>())
            .Returns(generated);

        _serverRepo.AddAsync(Arg.Any<McpServerDefinition>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<McpServerDefinition>());

        var req = new CreateServerRequest(
            "invoice-api", "Invoice API", "desc",
            "https://x/openapi.json", null,
            "https://invoice.example.com", "obo",
            new JsonObject { ["resource"] = "api://invoice-api/.default" },
            "all", "universal", 1440, "admin@corp.com");

        var sut = CreateSut();
        var result = await sut.RegisterAsync(req, CancellationToken.None);

        result.Name.Should().Be("invoice-api");
        result.ApprovalStatus.Should().Be("pending");
        await _specFetcher.Received(1).FetchAsync(SpecSource.FromUrl("https://x/openapi.json"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_DuplicateName_ThrowsConflict()
    {
        _serverRepo.GetByNameAsync("dup", Arg.Any<CancellationToken>())
            .Returns(new McpServerDefinition { Name = "dup" });

        var req = new CreateServerRequest(
            "dup", "Dup", null, null, "{}",
            "https://x", "obo", new JsonObject(), "all", "universal", 1440, null);

        var sut = CreateSut();
        Func<Task> act = () => sut.RegisterAsync(req, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task GetAsync_NotFound_Throws()
    {
        _serverRepo.GetByNameForAdminAsync("nope", Arg.Any<CancellationToken>()).Returns((McpServerDefinition?)null);
        var sut = CreateSut();
        Func<Task> act = () => sut.GetAsync("nope", CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ListAsync_ReturnsAllSorted()
    {
        _serverRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<McpServerDefinition>
        {
            new() { Name = "z" },
            new() { Name = "a" }
        });
        var sut = CreateSut();
        var result = await sut.ListAsync(CancellationToken.None);
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("a");
        result[1].Name.Should().Be("z");
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
