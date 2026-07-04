using System.Text.Json;

namespace McpGateway.BddTests.Support;

internal static class McpResponseReader
{
    public static JsonDocument? Parse(HttpResponseMessage response, string body)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var dataJson = ExtractFirstEventData(body);
            if (dataJson is null) return null;
            return JsonDocument.Parse(dataJson);
        }

        if (string.IsNullOrWhiteSpace(body)) return null;
        return JsonDocument.Parse(body);
    }

    private static string? ExtractFirstEventData(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("data: "))
            {
                return trimmed["data: ".Length..];
            }
        }
        return null;
    }
}
