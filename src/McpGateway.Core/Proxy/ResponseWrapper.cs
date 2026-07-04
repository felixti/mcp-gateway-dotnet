namespace McpGateway.Core.Proxy;

public class ResponseWrapper
{
    private const int MaxResponseLength = 10 * 1024;

    public async Task<ToolCallResult> WrapAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var truncated = body.Length > MaxResponseLength ? body[..MaxResponseLength] : body;

        if (response.IsSuccessStatusCode)
        {
            return new ToolCallResult
            {
                IsError = false,
                Content = [new ToolCallContent { Type = "text", Text = truncated }]
            };
        }

        return new ToolCallResult
        {
            IsError = true,
            Content = [new ToolCallContent { Type = "text", Text = $"[HTTP {(int)response.StatusCode}] {truncated}" }]
        };
    }
}
