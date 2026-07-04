namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Describes what changed in a single tool between two spec versions.
/// Empty <see cref="ChangedFields"/> list means metadata matched (still considered
/// present in the diff, but no admin review required).
/// </summary>
public sealed record SpecToolChange(
    string ToolName,
    string HttpMethod,
    string HttpPath,
    IReadOnlyList<string> ChangedFields);
