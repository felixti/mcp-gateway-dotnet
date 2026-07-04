using System.Collections.Concurrent;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.ToolStore;

public class InMemoryToolStore : IToolStore
{
    private readonly ConcurrentDictionary<string, McpServerDefinition> _servers = new();

    public McpServerDefinition? GetServer(string name)
        => _servers.TryGetValue(name, out var definition) ? definition : null;

    public IReadOnlyCollection<McpServerDefinition> GetAllServers()
        => _servers.Values.ToList().AsReadOnly();

    public void AddServer(McpServerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);

        _servers[definition.Name] = definition;
    }

    public void UpdateServer(McpServerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);

        _servers[definition.Name] = definition;
    }

    public bool RemoveServer(string name)
        => _servers.TryRemove(name, out _);

    public bool Contains(string name)
        => _servers.ContainsKey(name);
}
