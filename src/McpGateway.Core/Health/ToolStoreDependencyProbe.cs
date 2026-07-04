using McpGateway.Core.ToolStore;

namespace McpGateway.Core.Health;

public class ToolStoreDependencyProbe : IDependencyProbe
{
    private readonly IToolStore _toolStore;

    public ToolStoreDependencyProbe(IToolStore toolStore)
    {
        _toolStore = toolStore;
    }

    public string Name => "tool_store";

    public Task<ProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        var servers = _toolStore.GetAllServers();
        if (servers.Count == 0)
        {
            return Task.FromResult(ProbeResult.Failure("tool store has no server definitions loaded."));
        }

        return Task.FromResult(ProbeResult.Success());
    }
}
