using McpGateway.Core.ServerDefinitions;
using Microsoft.OpenApi.Models;

namespace McpGateway.Core.ToolGeneration;

public interface IToolGenerator
{
    IReadOnlyList<GeneratedTool> Generate(OpenApiDocument document, ClientProfile profile);
    IReadOnlyList<GeneratedTool> Generate(string openApiSpec, ClientProfile profile);
}
