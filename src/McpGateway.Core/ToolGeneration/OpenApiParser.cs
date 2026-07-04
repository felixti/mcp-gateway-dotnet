using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace McpGateway.Core.ToolGeneration;

public class OpenApiParser
{
    public OpenApiDocument Parse(string content)
    {
        var reader = new OpenApiStringReader();
        var result = reader.Read(content, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            var messages = string.Join("; ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"OpenAPI parse errors: {messages}");
        }

        return result;
    }

    public OpenApiDocument ParseYaml(string content)
    {
        var reader = new OpenApiStringReader();
        var result = reader.Read(content, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            var messages = string.Join("; ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"OpenAPI parse errors: {messages}");
        }

        return result;
    }
}
