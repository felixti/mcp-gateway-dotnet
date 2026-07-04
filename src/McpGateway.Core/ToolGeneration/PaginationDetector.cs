using Microsoft.OpenApi.Models;

namespace McpGateway.Core.ToolGeneration;

public class PaginationDetector
{
    private static readonly HashSet<string> LimitNames = new(StringComparer.OrdinalIgnoreCase) { "limit", "page_size", "pageSize", "per_page", "perPage" };
    private static readonly HashSet<string> OffsetNames = new(StringComparer.OrdinalIgnoreCase) { "offset", "page", "page_number", "pageNumber", "cursor" };

    public string? Detect(OpenApiOperation operation)
    {
        var parameters = operation.Parameters?.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var limit = parameters.FirstOrDefault(p => LimitNames.Contains(p));
        var offset = parameters.FirstOrDefault(p => OffsetNames.Contains(p));

        if (limit is null && offset is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (limit is not null)
        {
            parts.Add($"supports pagination via `{limit}` parameter");
        }
        if (offset is not null)
        {
            parts.Add($"page control via `{offset}` parameter");
        }

        return "Pagination: " + string.Join("; ", parts) + ".";
    }
}
