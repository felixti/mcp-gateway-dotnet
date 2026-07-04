using FluentAssertions;
using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpGateway.UnitTests.Management;

public class ClientProfileServiceTests
{
    private readonly IServerDefinitionRepository _serverRepo = Substitute.For<IServerDefinitionRepository>();
    private readonly IToolStore _toolStore = Substitute.For<IToolStore>();

    private ClientProfileService CreateSut() => new(_serverRepo, _toolStore, NullLogger<ClientProfileService>.Instance);

    [Fact]
    public async Task SetAsync_UpdatesProfileAndReloadsTools()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            ClientProfile = ClientProfile.Universal,
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "approved"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        await sut.SetAsync("inv", ClientProfile.Cursor, CancellationToken.None);

        await _serverRepo.Received(1).UpdateAsync(
            Arg.Is<McpServerDefinition>(d => d.ClientProfile == ClientProfile.Cursor),
            Arg.Any<CancellationToken>());
        _toolStore.Received(1).UpdateServer(Arg.Is<McpServerDefinition>(d => d.ClientProfile == ClientProfile.Cursor));
    }

    [Fact]
    public async Task SetAsync_NotApproved_DoesNotReloadStore()
    {
        var def = new McpServerDefinition
        {
            Id = Guid.NewGuid(),
            Name = "inv",
            ClientProfile = ClientProfile.Universal,
            SpecContent = "{}",
            SpecHash = "h",
            BaseUrl = "https://x",
            AuthStrategy = "obo",
            ApprovalStatus = "pending"
        };
        _serverRepo.GetByNameForAdminAsync("inv", Arg.Any<CancellationToken>()).Returns(def);

        var sut = CreateSut();
        await sut.SetAsync("inv", ClientProfile.Claude, CancellationToken.None);

        _toolStore.DidNotReceive().UpdateServer(Arg.Any<McpServerDefinition>());
    }
}
