using System.Reflection;
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Management.Services;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class GatewayApiKeyServiceTests
{
    private readonly IGatewayApiKeyRepository _keyRepo = Substitute.For<IGatewayApiKeyRepository>();
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();

    private GatewayApiKeyService CreateSut() => new(_keyRepo, _serverRepo);

    private static McpServerDefinition SampleServer() => new()
    {
        Id = Guid.NewGuid(),
        Name = "inv",
        SpecContent = "{}",
        SpecHash = "h",
        BaseUrl = "https://x",
        AuthStrategy = "obo"
    };

    [Fact]
    public async Task IssueAsync_ReturnsFullKeyMatchingPrefix()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _keyRepo.AddAsync(Arg.Any<GatewayApiKey>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<GatewayApiKey>());

        var sut = CreateSut();
        var result = await sut.IssueAsync("inv", new CreateApiKeyRequest("ci-runner", new[] { "inv" }), CancellationToken.None);

        result.FullKey.Should().StartWith("mgk_");
        result.FullKey.Length.Should().BeGreaterThan(40);
        result.KeyPrefix.Should().HaveLength(12);
        result.Scopes.Should().Contain("inv");
        await _keyRepo.Received(1).AddAsync(
            Arg.Is<GatewayApiKey>(k => k.KeyHash.StartsWith("$2") && k.Name == "ci-runner"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueAsync_ServerNotFound_Throws()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns((McpServerDefinition?)null);
        var sut = CreateSut();
        Func<Task> act = () => sut.IssueAsync("inv", new CreateApiKeyRequest("k", new[] { "inv" }), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ListAsync_NoServer_Throws()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns((McpServerDefinition?)null);
        var sut = CreateSut();
        Func<Task> act = () => sut.ListAsync("inv", CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ListAsync_ReturnsSummariesWithHashesStripped()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _keyRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<GatewayApiKey>
            {
                new() { Id = Guid.NewGuid(), KeyHash = "$2a$11$shouldnotbeexposed", KeyPrefix = "mgk_abc1", Name = "k1", Scopes = new[] { "inv" }, CreatedAt = DateTime.UtcNow }
            });

        var sut = CreateSut();
        var result = await sut.ListAsync("inv", CancellationToken.None);

        result.Should().ContainSingle();
        result[0].KeyPrefix.Should().Be("mgk_abc1");
        result[0].GetType().GetProperty("KeyHash", BindingFlags.Public | BindingFlags.Instance).Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_CallsRepo()
    {
        var id = Guid.NewGuid();
        var sut = CreateSut();
        await sut.RevokeAsync("inv", id, CancellationToken.None);
        await _keyRepo.Received(1).RevokeAsync(id, Arg.Any<CancellationToken>());
    }
}
