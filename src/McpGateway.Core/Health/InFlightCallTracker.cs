namespace McpGateway.Core.Health;

public class InFlightCallTracker : IInFlightCallTracker
{
    private int _inFlight;

    public int InFlightCount => Volatile.Read(ref _inFlight);

    public IDisposable Begin()
    {
        Interlocked.Increment(ref _inFlight);
        return new Scope(this);
    }

    public async Task WaitForDrainAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (InFlightCount > 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private void End() => Interlocked.Decrement(ref _inFlight);

    private sealed class Scope : IDisposable
    {
        private readonly InFlightCallTracker _tracker;
        private int _disposed;

        public Scope(InFlightCallTracker tracker)
        {
            _tracker = tracker;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _tracker.End();
            }
        }
    }
}
