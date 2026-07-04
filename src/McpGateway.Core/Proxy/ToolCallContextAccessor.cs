namespace McpGateway.Core.Proxy;

public class ToolCallContextAccessor
{
    private static readonly AsyncLocal<ToolCallContext?> _current = new();

    public ToolCallContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
