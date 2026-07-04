using System.Net;
using FluentAssertions;
using McpGateway.Core.SpecManagement;

namespace McpGateway.UnitTests.SpecManagement;

public class SpecFetcherTests
{
    [Fact]
    public async Task FetchAsync_FromStream_Json_ReturnsContentAndHash()
    {
        const string json = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"}}""";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var result = await fetcher.FetchAsync(SpecSource.FromStream(stream, "openapi.json"));

        result.Content.Should().Be(json);
        result.Format.Should().Be(SpecFormat.Json);
        result.Hash.Should().HaveLength(64);
        result.Hash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public async Task FetchAsync_FromStream_Yaml_DetectsYamlFormat()
    {
        const string yaml = "openapi: 3.0.0\ninfo:\n  title: T\n  version: 1.0\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var result = await fetcher.FetchAsync(SpecSource.FromStream(stream, "openapi.yaml"));

        result.Format.Should().Be(SpecFormat.Yaml);
        result.Content.Should().Be(yaml);
    }

    [Fact]
    public async Task FetchAsync_StreamYmlExtension_DetectsYaml()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("openapi: 3.0.0\n"));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var result = await fetcher.FetchAsync(SpecSource.FromStream(stream, "spec.yml"));

        result.Format.Should().Be(SpecFormat.Yaml);
    }

    [Fact]
    public async Task FetchAsync_JsonWithoutFilename_DetectsByContent()
    {
        const string json = """{"openapi":"3.0.0"}""";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var result = await fetcher.FetchAsync(SpecSource.FromStream(stream));

        result.Format.Should().Be(SpecFormat.Json);
    }

    [Fact]
    public async Task FetchAsync_FromUrl_FetchesContent()
    {
        const string json = """{"openapi":"3.0.0","info":{"title":"T","version":"1.0"}}""";
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://specs.example.com/") };
        var factory = new StubHttpClientFactory(httpClient);

        var fetcher = new SpecFetcher(factory);
        var result = await fetcher.FetchAsync(SpecSource.FromUrl("https://specs.example.com/openapi.json"));

        result.Content.Should().Be(json);
        result.Format.Should().Be(SpecFormat.Json);
    }

    [Fact]
    public async Task FetchAsync_FromUrl_NonSuccess_Throws()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://specs.example.com/") };
        var factory = new StubHttpClientFactory(httpClient);

        var fetcher = new SpecFetcher(factory);

        Func<Task> act = () => fetcher.FetchAsync(SpecSource.FromUrl("https://specs.example.com/missing.json"));
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task FetchAsync_HashIsDeterministic()
    {
        const string json = """{"openapi":"3.0.0"}""";

        using var stream1 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        using var stream2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        var fetcher = new SpecFetcher(httpClientFactory: null!);
        var first = await fetcher.FetchAsync(SpecSource.FromStream(stream1));
        var second = await fetcher.FetchAsync(SpecSource.FromStream(stream2));

        first.Hash.Should().Be(second.Hash);
    }

    [Fact]
    public void FetchAsync_NullSource_Throws()
    {
        var fetcher = new SpecFetcher(httpClientFactory: null!);
        Func<Task> act = async () => await fetcher.FetchAsync(null!);
        act.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_responder(request));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StubHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
