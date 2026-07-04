using System.Text.RegularExpressions;

namespace McpGateway.Core.ToolGeneration;

public partial class ToolNameResolver
{
    public string Resolve(string? operationId, string method, string path)
    {
        if (!string.IsNullOrWhiteSpace(operationId))
        {
            return Sanitize(operationId);
        }

        var sanitizedPath = path.Trim('/')
            .Replace("{", "")
            .Replace("}", "")
            .Replace("/", "_");

        return Sanitize($"{method.ToLowerInvariant()}_{sanitizedPath}");
    }

    private static string Sanitize(string value)
    {
        var lower = value.ToLowerInvariant();
        var withUnderscores = InvalidCharactersRegex().Replace(lower, "_");
        var trimmed = withUnderscores.Trim('_');
        var collapsed = Regex.Replace(trimmed, "_+", "_");
        return collapsed;
    }

    [GeneratedRegex(@"[^a-z0-9_]+")]
    private static partial Regex InvalidCharactersRegex();
}
