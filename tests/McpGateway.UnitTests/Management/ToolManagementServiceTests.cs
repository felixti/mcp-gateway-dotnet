using System.Text.Json.Nodes;
using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Management.Contracts;
using McpGateway.Management.Exceptions;
using McpGateway.Management.Services;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class ToolManagementServiceTests
{
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();
    private readonly IToolOverrideRepository _overrideRepo = Substitute.For<IToolOverrideRepository>();

    private ToolManagementService CreateSut() => new(_serverRepo, _overrideRepo);

    private static McpServerDefinition SampleServer() => new()
    {
        Id = Guid.NewGuid(),
        Name = "inv",
        SpecContent = "{}",
        SpecHash = "h",
        BaseUrl = "https://x",
        AuthStrategy = "obo",
        Tools = new List<ToolDefinition>
        {
            new() { ToolName = "ping", Description = "ping", HttpMethod = "GET", HttpPath = "/ping", InputSchema = "{}", AuthConfig = "{}", Visible = true }
        }
    };

    [Fact]
    public async Task ListAsync_ReturnsEffectiveDescriptionFromOverride()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _overrideRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ToolOverride>
            {
                new() { ToolName = "ping", DescriptionOverride = "custom", Visible = true }
            });

        var sut = CreateSut();
        var tools = await sut.ListAsync("inv", CancellationToken.None);

        tools.Should().ContainSingle();
        tools[0].EffectiveDescription.Should().Be("custom");
        tools[0].HasOverride.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_NoOverride_UsesSpecDescription()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _overrideRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ToolOverride>());

        var sut = CreateSut();
        var tools = await sut.ListAsync("inv", CancellationToken.None);

        tools[0].EffectiveDescription.Should().Be("ping");
        tools[0].HasOverride.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_HidesTool()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _overrideRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ToolOverride>());

        var sut = CreateSut();
        await sut.UpdateAsync("inv", "ping", new UpdateToolRequest(null, false), CancellationToken.None);

        await _overrideRepo.Received(1).UpsertAsync(
            Arg.Is<ToolOverride>(o => o.ToolName == "ping" && o.Visible == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        var sut = CreateSut();
        Func<Task> act = () => sut.UpdateAsync("inv", "missing", new UpdateToolRequest(null, true), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task PutOverrideAsync_Persists()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        var sut = CreateSut();
        await sut.PutOverrideAsync("inv", "ping", new PutOverrideRequest("hardened description"), CancellationToken.None);

        await _overrideRepo.Received(1).UpsertAsync(
            Arg.Is<ToolOverride>(o => o.DescriptionOverride == "hardened description" && o.Visible),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteOverrideAsync_Removes()
    {
        _serverRepo.GetByNameAsync("inv", Arg.Any<CancellationToken>()).Returns(SampleServer());
        _overrideRepo.ListByServerAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ToolOverride> { new() { ToolName = "ping", DescriptionOverride = "x", Visible = true } });

        var sut = CreateSut();
        await sut.DeleteOverrideAsync("inv", "ping", CancellationToken.None);

        await _overrideRepo.Received(1).DeleteAsync(Arg.Any<Guid>(), "ping", Arg.Any<CancellationToken>());
    }
}
