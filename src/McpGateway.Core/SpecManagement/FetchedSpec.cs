namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Result of fetching a spec — raw content plus deterministic SHA-256 hash
/// for change detection.
/// </summary>
public sealed record FetchedSpec(string Content, string Hash, SpecFormat Format);

public enum SpecFormat
{
    Json,
    Yaml
}
