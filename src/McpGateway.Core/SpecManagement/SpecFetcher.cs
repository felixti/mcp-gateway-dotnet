using System.Security.Cryptography;
using System.Text;

namespace McpGateway.Core.SpecManagement;

public class SpecFetcher : ISpecFetcher
{
    public const string HttpClientName = "spec-fetcher";
    private static readonly HashSet<string> YamlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".yaml", ".yml"
    };
    private static readonly HashSet<string> JsonExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json"
    };

    private readonly IHttpClientFactory? _httpClientFactory;

    public SpecFetcher(IHttpClientFactory? httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<FetchedSpec> FetchAsync(SpecSource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Stream is not null)
        {
            return await ReadFromStreamAsync(source.Stream, source.FileName, ct);
        }

        if (source.Url is not null)
        {
            return await FetchFromUrlAsync(source.Url, ct);
        }

        throw new InvalidOperationException("SpecSource must have either Stream or Url set.");
    }

    private async Task<FetchedSpec> FetchFromUrlAsync(string url, CancellationToken ct)
    {
        if (_httpClientFactory is null)
        {
            throw new InvalidOperationException(
                "HttpClientFactory is required for URL fetch. Register it via AddHttpClient().");
        }

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(ct);
        var fileName = ExtractFileNameFromUrl(url);
        var format = DetectFormat(content, fileName);

        return new FetchedSpec(content, ComputeHash(content), format);
    }

    private static async Task<FetchedSpec> ReadFromStreamAsync(Stream stream, string? fileName, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(ct);
        var format = DetectFormat(content, fileName);

        return new FetchedSpec(content, ComputeHash(content), format);
    }

    private static SpecFormat DetectFormat(string content, string? fileName)
    {
        if (fileName is not null)
        {
            var ext = Path.GetExtension(fileName);
            if (YamlExtensions.Contains(ext))
            {
                return SpecFormat.Yaml;
            }
            if (JsonExtensions.Contains(ext))
            {
                return SpecFormat.Json;
            }
        }

        var trimmed = content.AsSpan().TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return SpecFormat.Json;
        }

        return SpecFormat.Yaml;
    }

    private static string ExtractFileNameFromUrl(string url)
    {
        var queryIndex = url.IndexOfAny(['?', '#']);
        var path = queryIndex >= 0 ? url[..queryIndex] : url;
        return Path.GetFileName(path);
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
