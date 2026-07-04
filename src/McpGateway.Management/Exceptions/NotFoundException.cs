namespace McpGateway.Management.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string resource, string key)
        : base($"{resource} '{key}' not found.") { }
}
