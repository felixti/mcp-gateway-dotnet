namespace McpGateway.Core.Proxy;

public class ToolCallResult
{
    public bool IsError { get; set; }
    public IReadOnlyList<ToolCallContent> Content { get; set; } = [];
}

public class ToolCallContent
{
    public string Type { get; set; } = "text";
    public string Text { get; set; } = null!;
}
