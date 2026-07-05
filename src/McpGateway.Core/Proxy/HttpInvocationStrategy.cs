using McpGateway.Core.ServerDefinitions;

namespace McpGateway.Core.Proxy;

public class HttpInvocationStrategy : IToolInvocationStrategy
{
    private readonly HttpRequestBuilder _requestBuilder;
    private readonly ResponseWrapper _responseWrapper;
    private readonly HttpClient _httpClient;

    public HttpInvocationStrategy(
        HttpRequestBuilder requestBuilder,
        ResponseWrapper responseWrapper,
        HttpClient httpClient)
    {
        _requestBuilder = requestBuilder;
        _responseWrapper = responseWrapper;
        _httpClient = httpClient;
    }

    public SourceType SourceType => SourceType.OpenApi;

    public async Task<ToolCallResult> InvokeAsync(
        McpServerDefinition server,
        ToolDefinition tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct = default)
    {
        var request = _requestBuilder.Build(server.BaseUrl, tool, arguments);
        var response = await _httpClient.SendAsync(request, ct);
        var result = await _responseWrapper.WrapAsync(response);
        result.HttpStatus = (int)response.StatusCode;
        return result;
    }
}
