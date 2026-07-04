using McpGateway.Core.ToolGeneration;

namespace McpGateway.Management.Exceptions;

public class OpenApiSpecValidationException : Exception
{
    public SpecValidationReport Report { get; }

    public OpenApiSpecValidationException(SpecValidationReport report)
        : base("OpenAPI spec validation failed.")
    {
        Report = report;
    }
}
