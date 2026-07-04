using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolStore;
using McpGateway.Management.Exceptions;
using Microsoft.Extensions.Logging;

namespace McpGateway.Management.Services;

public class ClientProfileService
{
    private readonly IServerDefinitionRepository _serverRepo;
    private readonly IToolStore _toolStore;
    private readonly ILogger<ClientProfileService> _logger;

    public ClientProfileService(
        IServerDefinitionRepository serverRepo,
        IToolStore toolStore,
        ILogger<ClientProfileService> logger)
    {
        _serverRepo = serverRepo;
        _toolStore = toolStore;
        _logger = logger;
    }

    public async Task SetAsync(string serverName, ClientProfile profile, CancellationToken ct)
    {
        var def = await _serverRepo.GetByNameForAdminAsync(serverName, ct)
            ?? throw new NotFoundException("Server definition", serverName);

        def.ClientProfile = profile;
        def.UpdatedAt = DateTime.UtcNow;
        await _serverRepo.UpdateAsync(def, ct);

        if (def.ApprovalStatus == "approved" && def.Status == "active")
        {
            _toolStore.UpdateServer(def);
            _logger.LogInformation("Client profile updated and tool store refreshed for {Server}.", serverName);
        }
    }
}
