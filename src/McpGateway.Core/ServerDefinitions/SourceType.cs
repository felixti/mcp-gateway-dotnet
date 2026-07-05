using System.Text.Json.Serialization;

namespace McpGateway.Core.ServerDefinitions;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceType
{
    OpenApi,
    McpUpstream
}

public static class SourceTypeExtensions
{
    public static string ToCanonicalString(this SourceType type) => type switch
    {
        SourceType.OpenApi => "openapi",
        SourceType.McpUpstream => "mcp-upstream",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
}
