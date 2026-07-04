namespace McpGateway.Core.ToolGeneration;

public record SpecValidationIssue(
    string Pointer,
    string Code,
    string Message,
    string Severity);

public record SpecValidationReport(
    IReadOnlyList<SpecValidationIssue> Errors,
    IReadOnlyList<SpecValidationIssue> Warnings);
