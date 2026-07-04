using System.Text.Json.Nodes;
using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.ToolGeneration;

public class SchemaTransformer
{
    private const int CursorMaxTitleLength = 60;

    public JsonNode Transform(JsonNode schema, ClientProfile profile, JsonNode? components)
    {
        var clone = JsonNode.Parse(schema.ToJsonString())
            ?? throw new InvalidOperationException("Failed to clone schema.");

        InlineRefs(clone, components);

        if (profile is ClientProfile.Universal or ClientProfile.Cursor)
        {
            ReplaceAnyOfWithOneOf(clone);
        }

        if (profile == ClientProfile.Cursor)
        {
            TruncateTitles(clone, CursorMaxTitleLength);
        }

        return clone;
    }

    private static void InlineRefs(JsonNode node, JsonNode? components)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("$ref") && components is not null)
            {
                var refPath = obj["$ref"]!.GetValue<string>();
                var resolved = ResolveRef(components, refPath);
                if (resolved is not null)
                {
                    obj.Remove("$ref");
                    foreach (var property in resolved.AsObject())
                    {
                        obj[property.Key] = property.Value?.DeepClone();
                    }
                }
            }

            foreach (var property in obj)
            {
                if (property.Value is not null)
                {
                    InlineRefs(property.Value, components);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    InlineRefs(item, components);
                }
            }
        }
    }

    private static JsonNode? ResolveRef(JsonNode components, string refPath)
    {
        if (!refPath.StartsWith("#/components/"))
        {
            return null;
        }

        var parts = refPath.TrimStart('#', '/').Split('/');
        var current = components;
        foreach (var part in parts.Skip(1))
        {
            current = current[part];
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static void ReplaceAnyOfWithOneOf(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("anyOf"))
            {
                var anyOf = obj["anyOf"]!;
                obj.Remove("anyOf");
                obj["oneOf"] = anyOf.DeepClone();
            }

            foreach (var property in obj)
            {
                if (property.Value is not null)
                {
                    ReplaceAnyOfWithOneOf(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    ReplaceAnyOfWithOneOf(item);
                }
            }
        }
    }

    private static void TruncateTitles(JsonNode node, int maxLength)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("title") && obj["title"]?.GetValue<string>() is { } title)
            {
                obj["title"] = title.Length > maxLength ? title[..maxLength] : title;
            }

            foreach (var property in obj)
            {
                if (property.Value is not null)
                {
                    TruncateTitles(property.Value, maxLength);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    TruncateTitles(item, maxLength);
                }
            }
        }
    }
}
