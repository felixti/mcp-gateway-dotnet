namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Result of diffing the tools generated from two spec versions.
/// </summary>
public sealed record SpecDiffResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<SpecToolChange> Changed)
{
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Changed.Count > 0;

    public string ToJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            added = Added,
            removed = Removed,
            changed = Changed.Select(c => new
            {
                toolName = c.ToolName,
                httpMethod = c.HttpMethod,
                httpPath = c.HttpPath,
                changedFields = c.ChangedFields
            }).ToArray()
        });
    }
}
