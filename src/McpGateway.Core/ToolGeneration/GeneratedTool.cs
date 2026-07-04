using System.Text.Json.Nodes;

namespace McpGateway.Core.ToolGeneration;

public class GeneratedTool
{
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string HttpMethod { get; set; } = null!;
    public string HttpPath { get; set; } = null!;
    public JsonNode InputSchema { get; set; } = new JsonObject();
    public JsonNode? OutputSchema { get; set; }
    public string AuthConfig { get; set; } = "{}";
    public bool Visible { get; set; } = true;
}
