using McpGateway.Core.Repositories;

namespace McpGateway.Core.ToolStore;

public class ToolStoreInitializer
{
    private readonly IToolStore _toolStore;
    private readonly IServerDefinitionRepository _repository;

    public ToolStoreInitializer(IToolStore toolStore, IServerDefinitionRepository repository)
    {
        _toolStore = toolStore;
        _repository = repository;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var approvedServers = await _repository.ListApprovedAsync(ct);

        foreach (var server in approvedServers)
        {
            _toolStore.AddServer(server);
        }
    }
}
