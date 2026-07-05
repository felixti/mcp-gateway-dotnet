using ModelContextProtocol.Client;

namespace McpGateway.UnitTests.McpUpstream;

public class SdkClientApiSmokeTest
{
    [Fact]
    public void Client_transport_types_are_reachable()
    {
        // Real SDK API surface for upstream MCP client calls.
        // The original plan assumed ModelContextProtocol.Protocol.HttpClientTransport,
        // but the 2.0.0-preview.1 package places the client transport types under
        // ModelContextProtocol.Client and uses HttpTransportMode to select Streamable HTTP.
        _ = typeof(McpClient);
        _ = typeof(HttpClientTransport);
        _ = typeof(HttpClientTransportOptions);
        _ = HttpTransportMode.StreamableHttp;
    }
}
