using McpGateway.Core.Repositories;
using McpGateway.Core.ServerDefinitions;
using Microsoft.Extensions.Logging;

namespace McpGateway.Core.SpecManagement;

public class SpecRefresher : ISpecRefresher
{
    private readonly IServerDefinitionRepository _serverRepository;
    private readonly ServerSpecRefresher _singleRefresher;
    private readonly ILogger<SpecRefresher> _logger;

    public SpecRefresher(
        IServerDefinitionRepository serverRepository,
        ServerSpecRefresher singleRefresher,
        ILogger<SpecRefresher> logger)
    {
        _serverRepository = serverRepository;
        _singleRefresher = singleRefresher;
        _logger = logger;
    }

    public async Task<SpecRefreshOutcome> RefreshAsync(string serverName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        var server = await _serverRepository.GetByNameAsync(serverName, ct);
        if (server is null)
        {
            return new SpecRefreshOutcome(
                SpecRefreshStatus.Failed,
                serverName,
                OldHash: null,
                NewHash: null,
                Error: "Server definition not found.");
        }

        var outcome = await _singleRefresher.RefreshAsync(server, ct);
        LogOutcome(outcome);
        return outcome;
    }

    public async Task<IReadOnlyList<SpecRefreshOutcome>> RefreshAllAsync(CancellationToken ct = default)
    {
        var allServers = await _serverRepository.ListAsync(ct);
        var activeServers = allServers
            .Where(s => s.Status == "active")
            .ToList();

        var outcomes = new List<SpecRefreshOutcome>(activeServers.Count);
        foreach (var server in activeServers)
        {
            var outcome = await _singleRefresher.RefreshAsync(server, ct);
            outcomes.Add(outcome);
        }

        return outcomes;
    }

    private void LogOutcome(SpecRefreshOutcome outcome)
    {
        switch (outcome.Status)
        {
            case SpecRefreshStatus.Updated:
                _logger.LogInformation(
                    "Spec refresh: {Server} updated ({Old} -> {New})",
                    outcome.ServerName, outcome.OldHash, outcome.NewHash);
                break;
            case SpecRefreshStatus.Unchanged:
                _logger.LogDebug(
                    "Spec refresh: {Server} unchanged ({Hash})",
                    outcome.ServerName, outcome.NewHash);
                break;
            case SpecRefreshStatus.Failed:
                _logger.LogWarning(
                    "Spec refresh: {Server} failed: {Error}",
                    outcome.ServerName, outcome.Error);
                break;
            case SpecRefreshStatus.NoSpecSource:
                _logger.LogDebug(
                    "Spec refresh: {Server} has no SpecSourceUrl, skipping",
                    outcome.ServerName);
                break;
        }
    }
}
