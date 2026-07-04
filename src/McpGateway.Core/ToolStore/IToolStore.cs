using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.ToolStore;

public interface IToolStore
{
    McpServerDefinition? GetServer(string name);
    IReadOnlyCollection<McpServerDefinition> GetAllServers();
    void AddServer(McpServerDefinition definition);
    void UpdateServer(McpServerDefinition definition);
    bool RemoveServer(string name);
    bool Contains(string name);
}
