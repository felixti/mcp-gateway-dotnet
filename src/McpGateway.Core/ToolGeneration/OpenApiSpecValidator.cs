using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using McpGateway.Core.ServerDefinitions;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Writers;

namespace McpGateway.Core.ToolGeneration;

public interface IOpenApiSpecValidator
{
    SpecValidationReport Validate(string specContent, ClientProfile profile);
}

public sealed class OpenApiSpecValidator : IOpenApiSpecValidator
{
    private static readonly Regex PathPlaceholderRegex = new(@"\{(\w+)\}", RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedOpenApiVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "3.0.0", "3.0.1", "3.0.2", "3.0.3"
    };

    public SpecValidationReport Validate(string specContent, ClientProfile profile)
    {
        var errors = new List<SpecValidationIssue>();
        var warnings = new List<SpecValidationIssue>();

        OpenApiDocument? document;
        try
        {
            var reader = new OpenApiStringReader();
            document = reader.Read(specContent, out var diagnostic);

            foreach (var error in diagnostic.Errors)
            {
                var (code, message) = ClassifyParseError(error.Message);
                errors.Add(Issue("", code, message, "error"));
            }

            foreach (var warning in diagnostic.Warnings)
            {
                warnings.Add(Issue("", "reader_warning", warning.Message, "warning"));
            }
        }
        catch (Exception ex)
        {
            errors.Add(Issue("", "parse_error", $"Could not parse OpenAPI spec: {ex.Message}", "error"));
            return new SpecValidationReport(errors, warnings);
        }

        if (document is null)
        {
            errors.Add(Issue("", "parse_error", "OpenAPI document is null after parsing.", "error"));
            return new SpecValidationReport(errors, warnings);
        }

        ValidateVersion(document, warnings);
        ValidatePaths(document, errors, warnings);
        ValidateReferences(document, errors, warnings);

        return new SpecValidationReport(errors, warnings);
    }

    private static (string Code, string Message) ClassifyParseError(string message)
    {
        if (message.Contains("Invalid Reference identifier", StringComparison.OrdinalIgnoreCase))
        {
            return ("unresolved_reference", $"OpenAPI parse error: {message}");
        }

        return ("parse_error", $"OpenAPI parse error: {message}");
    }

    private static void ValidateVersion(OpenApiDocument document, List<SpecValidationIssue> warnings)
    {
        if (document.Info?.Version is not null &&
            !SupportedOpenApiVersions.Contains(document.Info.Version))
        {
            warnings.Add(Issue("", "unsupported_openapi_version",
                $"OpenAPI version '{document.Info.Version}' is not in the supported 3.0.x set. The spec may not generate correct tools.", "warning"));
        }
    }

    private static void ValidatePaths(
        OpenApiDocument document,
        List<SpecValidationIssue> errors,
        List<SpecValidationIssue> warnings)
    {
        if (document.Paths is null || document.Paths.Count == 0)
        {
            errors.Add(Issue("/paths", "empty_paths", "Spec has no paths. At least one path with operations is required.", "error"));
            return;
        }

        var toolNames = new Dictionary<string, (string Path, string Method)>(StringComparer.OrdinalIgnoreCase);

        foreach (var pathItem in document.Paths)
        {
            var path = pathItem.Key;
            var pathPointer = $"/paths/{EscapePointer(path)}";

            if (pathItem.Value.Operations is null || pathItem.Value.Operations.Count == 0)
            {
                errors.Add(Issue(pathPointer, "empty_operations", $"Path '{path}' has no operations.", "error"));
                continue;
            }

            var declaredPathParams = pathItem.Value.Parameters
                .Where(p => p.In == ParameterLocation.Path)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var operationEntry in pathItem.Value.Operations)
            {
                foreach (var param in operationEntry.Value.Parameters?.Where(p => p.In == ParameterLocation.Path) ?? [])
                {
                    declaredPathParams.Add(param.Name);
                }
            }

            var placeholders = PathPlaceholderRegex
                .Matches(path)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();

            foreach (var placeholder in placeholders)
            {
                if (!declaredPathParams.Contains(placeholder))
                {
                    errors.Add(Issue(pathPointer, "missing_path_parameter",
                        $"Path placeholder '{{{placeholder}}}' in '{path}' has no matching required path parameter.", "error"));
                }
            }

            foreach (var operationEntry in pathItem.Value.Operations)
            {
                var method = operationEntry.Key.ToString().ToUpperInvariant();
                var operationPointer = $"{pathPointer}/{method.ToLowerInvariant()}";
                var operation = operationEntry.Value;

                ValidateOperation(
                    operation,
                    path,
                    method,
                    operationPointer,
                    placeholders,
                    declaredPathParams,
                    toolNames,
                    errors,
                    warnings);
            }
        }
    }

    private static void ValidateOperation(
        OpenApiOperation operation,
        string path,
        string method,
        string operationPointer,
        IReadOnlyList<string> pathPlaceholders,
        IReadOnlySet<string> declaredPathParams,
        Dictionary<string, (string Path, string Method)> toolNames,
        List<SpecValidationIssue> errors,
        List<SpecValidationIssue> warnings)
    {
        var operationParams = operation.Parameters ?? new List<OpenApiParameter>();

        foreach (var param in operationParams.Where(p => p.In == ParameterLocation.Path))
        {
            if (!pathPlaceholders.Contains(param.Name, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(Issue(operationPointer, "orphan_path_parameter",
                    $"Path parameter '{param.Name}' is declared but does not appear as a placeholder in '{path}'.", "error"));
            }

            if (!param.Required)
            {
                warnings.Add(Issue($"{operationPointer}/parameters/{param.Name}", "path_parameter_not_required",
                    $"Path parameter '{param.Name}' should be marked required.", "warning"));
            }
        }

        foreach (var param in operationParams.Where(p => p.In is ParameterLocation.Header or ParameterLocation.Cookie))
        {
            warnings.Add(Issue($"{operationPointer}/parameters/{param.Name}", "ignored_header_parameter",
                $"Parameter '{param.Name}' with location '{param.In}' is exposed in the tool schema but will be sent as a query parameter, not as a header/cookie.", "warning"));
        }

        var hasOperationId = !string.IsNullOrWhiteSpace(operation.OperationId);
        if (!hasOperationId)
        {
            warnings.Add(Issue(operationPointer, "missing_operation_id",
                $"Operation on {method} {path} has no operationId. A synthetic tool name will be used.", "warning"));
        }

        var toolName = new ToolNameResolver().Resolve(operation.OperationId, method, path);
        if (string.IsNullOrEmpty(toolName))
        {
            errors.Add(Issue(operationPointer, "empty_tool_name",
                $"OperationId '{operation.OperationId}' produces an empty tool name after sanitization.", "error"));
        }
        else if (toolNames.TryGetValue(toolName, out var existing))
        {
            errors.Add(Issue(operationPointer, "duplicate_tool_name",
                $"Tool name '{toolName}' conflicts with {existing.Method} {existing.Path}. operationIds must be unique after sanitization.", "error"));
        }
        else
        {
            toolNames[toolName] = (path, method);
        }

        var hasSummary = !string.IsNullOrWhiteSpace(operation.Summary);
        var hasDescription = !string.IsNullOrWhiteSpace(operation.Description);
        if (!hasSummary && !hasDescription)
        {
            warnings.Add(Issue(operationPointer, "missing_summary_and_description",
                $"Operation {method} {path} has no summary or description. The tool description will fall back to the HTTP method and path.", "warning"));
        }

        if (operation.RequestBody is not null)
        {
            if (!operation.RequestBody.Content.ContainsKey("application/json"))
            {
                var mediaTypes = string.Join(", ", operation.RequestBody.Content.Keys);
                warnings.Add(Issue($"{operationPointer}/requestBody", "unsupported_request_body",
                    $"Request body has unsupported media type(s): {mediaTypes}. Only application/json is supported.", "warning"));
            }
            else if (operation.RequestBody.Content.TryGetValue("application/json", out var jsonBody))
            {
                if (operation.RequestBody.Required == false && jsonBody.Schema is not null)
                {
                    warnings.Add(Issue($"{operationPointer}/requestBody", "optional_body_forced_required",
                        "requestBody.required is false, but the gateway will treat the body as required in the tool schema.", "warning"));
                }
            }
        }

        var successResponse = operation.Responses
            .FirstOrDefault(r => r.Key.StartsWith("2", StringComparison.Ordinal));

        if (successResponse.Value is null)
        {
            warnings.Add(Issue($"{operationPointer}/responses", "no_json_response",
                $"Operation {method} {path} has no 2xx response. The tool will have no output schema.", "warning"));
        }
        else if (!successResponse.Value.Content.ContainsKey("application/json"))
        {
            warnings.Add(Issue($"{operationPointer}/responses/{successResponse.Key}", "non_json_response_body",
                $"2xx response for {method} {path} does not define application/json content. The tool will have no output schema.", "warning"));
        }
    }

    private static void ValidateReferences(
        OpenApiDocument document,
        List<SpecValidationIssue> errors,
        List<SpecValidationIssue> warnings)
    {
        if (document.Components is null)
        {
            return;
        }

        var schemas = document.Components.Schemas ?? new Dictionary<string, OpenApiSchema>();
        var visited = new HashSet<string>();
        var stack = new Stack<string>();

        foreach (var schema in schemas.Values)
        {
            CheckReferenceCycles(schema, schemas, visited, stack, errors);
        }

        CollectBrokenRefs(document, errors);
    }

    private static void CollectBrokenRefs(OpenApiDocument document, List<SpecValidationIssue> errors)
    {
        using var writer = new StringWriter();
        document.SerializeAsV3(new OpenApiJsonWriter(writer));
        var root = JsonNode.Parse(writer.ToString());
        if (root is null)
        {
            return;
        }

        var availableSchemas = document.Components?.Schemas?.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>();

        WalkForRefs(root, "", availableSchemas, errors);
    }

    private static void WalkForRefs(JsonNode node, string pointer, HashSet<string> availableSchemas, List<SpecValidationIssue> errors)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("$ref", out var refNode) && refNode is JsonValue refValue)
            {
                var refText = refValue.GetValue<string>();
                if (!refText.StartsWith("#/components/", StringComparison.Ordinal))
                {
                    errors.Add(Issue(pointer, "external_reference",
                        $"External or unsupported $ref '{refText}' found. Only #/components/ references are supported.", "error"));
                }
                else
                {
                    var refId = refText[("#/components/schemas/".Length)..];
                    if (!availableSchemas.Contains(refId))
                    {
                        errors.Add(Issue(pointer, "unresolved_reference",
                            $"Referenced schema '{refId}' was not found in components/schemas.", "error"));
                    }
                }
            }

            foreach (var property in obj)
            {
                if (property.Value is not null)
                {
                    var childPointer = string.IsNullOrEmpty(pointer)
                        ? $"/{property.Key}"
                        : $"{pointer}/{EscapePointer(property.Key)}";
                    WalkForRefs(property.Value, childPointer, availableSchemas, errors);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not null)
                {
                    WalkForRefs(array[i]!, $"{pointer}/{i}", availableSchemas, errors);
                }
            }
        }
    }

    private static void CheckReferenceCycles(
        OpenApiSchema schema,
        IDictionary<string, OpenApiSchema> schemas,
        HashSet<string> visited,
        Stack<string> stack,
        List<SpecValidationIssue> errors)
    {
        if (schema.Reference is not null)
        {
            var refId = schema.Reference.Id;
            if (stack.Contains(refId))
            {
                errors.Add(Issue($"/components/schemas/{EscapePointer(refId)}", "recursive_reference",
                    $"Schema '{refId}' has a recursive $ref cycle.", "error"));
                return;
            }

            if (!visited.Add(refId))
            {
                return;
            }

            if (schemas.TryGetValue(refId, out var resolved))
            {
                stack.Push(refId);
                WalkSchemaStructure(resolved, schemas, visited, stack, errors);
                stack.Pop();
            }

            return;
        }

        WalkSchemaStructure(schema, schemas, visited, stack, errors);
    }

    private static void WalkSchemaStructure(
        OpenApiSchema schema,
        IDictionary<string, OpenApiSchema> schemas,
        HashSet<string> visited,
        Stack<string> stack,
        List<SpecValidationIssue> errors)
    {
        foreach (var property in schema.Properties.Values)
        {
            CheckReferenceCycles(property, schemas, visited, stack, errors);
        }

        if (schema.Items is not null)
        {
            CheckReferenceCycles(schema.Items, schemas, visited, stack, errors);
        }

        foreach (var subSchema in schema.AllOf.Union(schema.AnyOf).Union(schema.OneOf))
        {
            CheckReferenceCycles(subSchema, schemas, visited, stack, errors);
        }

        if (schema.AdditionalProperties is not null)
        {
            CheckReferenceCycles(schema.AdditionalProperties, schemas, visited, stack, errors);
        }
    }

    private static SpecValidationIssue Issue(string pointer, string code, string message, string severity)
        => new(pointer, code, message, severity);

    private static string EscapePointer(string segment)
    {
        var sb = new StringBuilder(segment);
        sb.Replace("~", "~0");
        sb.Replace("/", "~1");
        return sb.ToString();
    }
}
