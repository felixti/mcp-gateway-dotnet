using System.Text.RegularExpressions;

namespace McpGateway.Core.ToolGeneration;

public partial class DescriptionBuilder
{
    public string Build(string? summary, string? description, string? fallback)
    {
        var text = !string.IsNullOrWhiteSpace(summary)
            ? summary
            : !string.IsNullOrWhiteSpace(description)
                ? description
                : fallback ?? string.Empty;

        return WhitespaceRegex().Replace(text.Trim(), " ");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
