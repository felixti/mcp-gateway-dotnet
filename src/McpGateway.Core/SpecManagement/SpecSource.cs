namespace McpGateway.Core.SpecManagement;

/// <summary>
/// Describes where to fetch an OpenAPI spec from.
/// Exactly one of <see cref="Url"/> or <see cref="Stream"/> is populated.
/// </summary>
public sealed record SpecSource
{
    public string? Url { get; init; }
    public Stream? Stream { get; init; }
    public string? FileName { get; init; }

    public static SpecSource FromUrl(string url) =>
        new() { Url = url };

    public static SpecSource FromStream(Stream stream, string? fileName = null) =>
        new() { Stream = stream, FileName = fileName };
}
