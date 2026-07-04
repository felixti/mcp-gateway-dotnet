namespace McpGateway.Core.Proxy.Exceptions;

public class ToolNotFoundException : Exception
{
    public ToolNotFoundException(string serverName, string toolName)
        : base($"Tool '{toolName}' not found in server '{serverName}'.")
    {
        ServerName = serverName;
        ToolName = toolName;
    }

    public string ServerName { get; }
    public string ToolName { get; }
}
