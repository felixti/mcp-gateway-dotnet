using FluentAssertions;
using McpGateway.Core.Health;

namespace McpGateway.UnitTests.Health;

public class InFlightCallTrackerTests
{
    [Fact]
    public void Begin_IncrementsCount()
    {
        var tracker = new InFlightCallTracker();

        tracker.Begin();
        tracker.Begin();

        tracker.InFlightCount.Should().Be(2);
    }

    [Fact]
    public void Dispose_DecrementsCount()
    {
        var tracker = new InFlightCallTracker();
        tracker.Begin();
        tracker.Begin();
        tracker.Begin();

        var a = tracker.Begin();
        a.Dispose();
        tracker.InFlightCount.Should().Be(3);
    }

    [Fact]
    public void InFlightCount_StartsAtZero()
    {
        var tracker = new InFlightCallTracker();
        tracker.InFlightCount.Should().Be(0);
    }

    [Fact]
    public void WaitForDrainAsync_NoInFlight_ReturnsImmediately()
    {
        var tracker = new InFlightCallTracker();

        var task = tracker.WaitForDrainAsync(TimeSpan.FromSeconds(1));

        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForDrainAsync_DrainsWhenCountReachesZero()
    {
        var tracker = new InFlightCallTracker();
        var scope = tracker.Begin();

        var drainTask = tracker.WaitForDrainAsync(TimeSpan.FromSeconds(5));
        drainTask.IsCompleted.Should().BeFalse();

        scope.Dispose();
        await drainTask;

        drainTask.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForDrainAsync_TimesOutWhenCallsRemain()
    {
        var tracker = new InFlightCallTracker();
        using var scope = tracker.Begin();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await tracker.WaitForDrainAsync(TimeSpan.FromMilliseconds(150));
        sw.Stop();

        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(100));
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Begin_AfterDispose_BeginsNewScope()
    {
        var tracker = new InFlightCallTracker();
        var scope = tracker.Begin();
        scope.Dispose();

        var scope2 = tracker.Begin();
        tracker.InFlightCount.Should().Be(1);
        scope2.Dispose();
    }
}
