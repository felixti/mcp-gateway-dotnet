using System.Text;
using System.Text.Json;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Proxy;

public class HttpRequestBuilder
{
    public HttpRequestMessage Build(string baseUrl, ToolDefinition tool, IReadOnlyDictionary<string, object?> arguments)
    {
        if (string.IsNullOrWhiteSpace(tool.HttpPath) || string.IsNullOrWhiteSpace(tool.HttpMethod))
        {
            throw new InvalidOperationException($"Tool '{tool.ToolName}' has no HTTP coordinates; it is not an OpenAPI-backed tool.");
        }

        var path = SubstitutePathParameters(tool.HttpPath, arguments);
        var query = BuildQueryString(tool.HttpPath, arguments);
        var uri = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/') + query);

        var request = new HttpRequestMessage(new HttpMethod(tool.HttpMethod), uri);

        if (arguments.TryGetValue("body", out var body) && body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    private static string SubstitutePathParameters(string path, IReadOnlyDictionary<string, object?> arguments)
    {
        var result = path;
        foreach (var match in System.Text.RegularExpressions.Regex.Matches(path, @"\{(\w+)\}").Cast<System.Text.RegularExpressions.Match>())
        {
            var paramName = match.Groups[1].Value;
            if (arguments.TryGetValue(paramName, out var value) && value is not null)
            {
                result = result.Replace(match.Value, Uri.EscapeDataString(value.ToString()!));
            }
        }
        return result;
    }

    private static string BuildQueryString(string path, IReadOnlyDictionary<string, object?> arguments)
    {
        var pathParams = System.Text.RegularExpressions.Regex.Matches(path, @"\{(\w+)\}")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var queryParams = arguments
            .Where(a => a.Key != "body" && !pathParams.Contains(a.Key) && a.Value is not null)
            .Select(a => $"{a.Key}={Uri.EscapeDataString(a.Value!.ToString()!)}");

        var query = string.Join("&", queryParams);
        return string.IsNullOrEmpty(query) ? string.Empty : "?" + query;
    }
}
