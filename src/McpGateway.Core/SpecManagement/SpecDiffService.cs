using System.Text.Json;
using System.Text.Json.Nodes;
using McpGateway.Core.ServerDefinitions;
using McpGateway.Core.ToolGeneration;

namespace McpGateway.Core.SpecManagement;

public class SpecDiffService : ISpecDiffService
{
    private readonly IToolGenerator _toolGenerator;

    public SpecDiffService(IToolGenerator toolGenerator)
    {
        _toolGenerator = toolGenerator;
    }

    public SpecDiffResult Diff(string oldSpecContent, string newSpecContent, ClientProfile profile)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldSpecContent);
        ArgumentException.ThrowIfNullOrEmpty(newSpecContent);

        var oldTools = _toolGenerator.Generate(oldSpecContent, profile);
        var newTools = _toolGenerator.Generate(newSpecContent, profile);

        return ComputeDiff(oldTools, newTools);
    }

    internal static SpecDiffResult ComputeDiff(
        IReadOnlyList<GeneratedTool> oldTools,
        IReadOnlyList<GeneratedTool> newTools)
    {
        var oldByName = oldTools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var newByName = newTools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        var added = newByName.Keys.Except(oldByName.Keys, StringComparer.Ordinal).OrderBy(n => n).ToList();
        var removed = oldByName.Keys.Except(newByName.Keys, StringComparer.Ordinal).OrderBy(n => n).ToList();

        var changed = new List<SpecToolChange>();
        foreach (var name in oldByName.Keys.Intersect(newByName.Keys, StringComparer.Ordinal).OrderBy(n => n))
        {
            var oldTool = oldByName[name];
            var newTool = newByName[name];
            var changedFields = CompareFields(oldTool, newTool);
            if (changedFields.Count > 0)
            {
                changed.Add(new SpecToolChange(
                    ToolName: newTool.Name,
                    HttpMethod: newTool.HttpMethod,
                    HttpPath: newTool.HttpPath,
                    ChangedFields: changedFields));
            }
        }

        return new SpecDiffResult(added, removed, changed);
    }

    private static List<string> CompareFields(GeneratedTool oldTool, GeneratedTool newTool)
    {
        var fields = new List<string>();

        if (!string.Equals(oldTool.Description, newTool.Description, StringComparison.Ordinal))
        {
            fields.Add("description");
        }
        if (!string.Equals(oldTool.HttpMethod, newTool.HttpMethod, StringComparison.Ordinal))
        {
            fields.Add("httpMethod");
        }
        if (!string.Equals(oldTool.HttpPath, newTool.HttpPath, StringComparison.Ordinal))
        {
            fields.Add("httpPath");
        }
        if (!JsonNodesEqual(oldTool.InputSchema, newTool.InputSchema))
        {
            fields.Add("inputSchema");
        }
        if (!JsonNodesEqual(oldTool.OutputSchema, newTool.OutputSchema))
        {
            fields.Add("outputSchema");
        }

        return fields;
    }

    private static bool JsonNodesEqual(JsonNode? a, JsonNode? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return string.Equals(
            a.ToJsonString(SortOptions),
            b.ToJsonString(SortOptions),
            StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions SortOptions = new()
    {
        WriteIndented = false
    };
}
