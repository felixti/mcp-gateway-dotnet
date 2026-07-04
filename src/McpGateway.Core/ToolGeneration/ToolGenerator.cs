using System.Text.Json.Nodes;
using McpGateway.Core.ServerDefinitions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Models;

namespace McpGateway.Core.ToolGeneration;

public class ToolGenerator : IToolGenerator
{
    private readonly OpenApiParser _parser = new();
    private readonly ToolNameResolver _nameResolver = new();
    private readonly DescriptionBuilder _descriptionBuilder = new();
    private readonly PaginationDetector _paginationDetector = new();
    private readonly SchemaTransformer _schemaTransformer = new();

    public IReadOnlyList<GeneratedTool> Generate(string openApiSpec, ClientProfile profile)
    {
        var document = _parser.Parse(openApiSpec);
        return Generate(document, profile);
    }

    public IReadOnlyList<GeneratedTool> Generate(OpenApiDocument document, ClientProfile profile)
    {
        var componentsNode = ConvertComponentsToJsonNode(document);

        var tools = new List<GeneratedTool>();

        foreach (var pathItem in document.Paths)
        {
            foreach (var operation in pathItem.Value.Operations)
            {
                var tool = BuildTool(operation.Value, pathItem.Key, operation.Key, profile, componentsNode);
                tools.Add(tool);
            }
        }

        return tools;
    }

    private GeneratedTool BuildTool(OpenApiOperation operation, string path, OperationType method, ClientProfile profile, JsonNode? componentsNode)
    {
        var name = _nameResolver.Resolve(operation.OperationId, method.ToString().ToUpperInvariant(), path);
        var paginationNote = _paginationDetector.Detect(operation);
        var baseDescription = _descriptionBuilder.Build(
            operation.Summary,
            operation.Description,
            $"{method.ToString().ToUpperInvariant()} {path}");

        var description = paginationNote is not null
            ? $"{baseDescription} {paginationNote}"
            : baseDescription;

        var inputSchema = BuildInputSchema(operation, componentsNode, profile);
        var outputSchema = BuildOutputSchema(operation, componentsNode, profile);

        return new GeneratedTool
        {
            Name = name,
            Description = description,
            HttpMethod = method.ToString().ToUpperInvariant(),
            HttpPath = path,
            InputSchema = inputSchema,
            OutputSchema = outputSchema
        };
    }

    private JsonNode BuildInputSchema(OpenApiOperation operation, JsonNode? componentsNode, ClientProfile profile)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray()
        };

        foreach (var parameter in operation.Parameters ?? [])
        {
            var parameterSchema = parameter.Schema is not null
                ? _schemaTransformer.Transform(ConvertSchemaToJsonNode(parameter.Schema), profile, componentsNode)
                : new JsonObject();

            ((JsonObject)schema["properties"]!)[parameter.Name] = parameterSchema;

            if (parameter.Required)
            {
                ((JsonArray)schema["required"]!).Add(parameter.Name);
            }
        }

        if (operation.RequestBody?.Content.TryGetValue("application/json", out var mediaType) == true
            && mediaType.Schema is not null)
        {
            var bodySchema = _schemaTransformer.Transform(
                ConvertSchemaToJsonNode(mediaType.Schema),
                profile,
                componentsNode);

            ((JsonObject)schema["properties"]!)["body"] = bodySchema;
            ((JsonArray)schema["required"]!).Add("body");
        }

        return schema;
    }

    private JsonNode? BuildOutputSchema(OpenApiOperation operation, JsonNode? componentsNode, ClientProfile profile)
    {
        var successResponse = operation.Responses
            .FirstOrDefault(r => r.Key.StartsWith('2'))
            .Value;

        if (successResponse?.Content.TryGetValue("application/json", out var mediaType) != true
            || mediaType?.Schema is null)
        {
            return null;
        }

        return _schemaTransformer.Transform(
            ConvertSchemaToJsonNode(mediaType.Schema),
            profile,
            componentsNode);
    }

    private static JsonNode ConvertSchemaToJsonNode(OpenApiSchema schema)
    {
        using var stringWriter = new System.IO.StringWriter();
        var writer = new Microsoft.OpenApi.Writers.OpenApiJsonWriter(stringWriter);
        schema.SerializeAsV3(writer);
        return JsonNode.Parse(stringWriter.ToString()) ?? new JsonObject();
    }

    private static JsonNode? ConvertComponentsToJsonNode(OpenApiDocument document)
    {
        if (document.Components is null)
        {
            return null;
        }

        using var stringWriter = new System.IO.StringWriter();
        var writer = new Microsoft.OpenApi.Writers.OpenApiJsonWriter(stringWriter);
        document.SerializeAsV3(writer);
        var root = JsonNode.Parse(stringWriter.ToString());
        return root?["components"];
    }
}
