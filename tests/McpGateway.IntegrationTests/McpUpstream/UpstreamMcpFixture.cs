using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpGateway.IntegrationTests.McpUpstream;

[McpServerToolType]
public sealed class EchoTool
{
    [McpServerTool]
    public static string Echo(string msg) => $"echo:{msg}";
}

public sealed class UpstreamMcpFixture : IAsyncLifetime, IAsyncDisposable
{
    private WebApplication? _app;

    public string Endpoint { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "echo-upstream",
                Version = "1.0.0"
            };
        })
        .WithHttpTransport(opts => opts.Stateless = true)
        .WithTools<EchoTool>();

        _app = builder.Build();
        _app.MapMcp("/mcp");
        await _app.StartAsync();
        Endpoint = _app.Urls.First().TrimEnd('/');
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsyncCore();

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsyncCore());

    private async Task DisposeAsyncCore()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
