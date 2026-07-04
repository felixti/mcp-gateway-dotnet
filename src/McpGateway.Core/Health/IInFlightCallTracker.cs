namespace McpGateway.Core.Health;

public interface IInFlightCallTracker
{
    int InFlightCount { get; }
    IDisposable Begin();
    Task WaitForDrainAsync(TimeSpan timeout, CancellationToken ct = default);
}
