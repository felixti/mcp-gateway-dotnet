using FluentAssertions;
using McpGateway.Core.Health;

namespace McpGateway.UnitTests.Health;

public class ReadinessStateTests
{
    [Fact]
    public void InitialSnapshot_IsNotReady()
    {
        var state = new ReadinessState();

        var snapshot = state.Current;

        snapshot.IsReady.Should().BeFalse();
        snapshot.PostgresOk.Should().BeFalse();
        snapshot.StorageQueueOk.Should().BeFalse();
        snapshot.ToolStoreOk.Should().BeFalse();
        snapshot.LastCheckedAt.Should().BeNull();
    }

    [Fact]
    public void Update_StoresLatestSnapshot()
    {
        var state = new ReadinessState();
        var snapshot = new ReadinessSnapshot(
            IsReady: true,
            PostgresOk: true,
            StorageQueueOk: true,
            ToolStoreOk: true,
            LastCheckedAt: DateTime.UtcNow,
            PostgresError: null,
            StorageQueueError: null,
            ToolStoreError: null);

        state.Update(snapshot);

        var current = state.Current;
        current.IsReady.Should().BeTrue();
        current.PostgresOk.Should().BeTrue();
        current.StorageQueueOk.Should().BeTrue();
        current.ToolStoreOk.Should().BeTrue();
        current.LastCheckedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkNotReady_OverwritesSnapshot()
    {
        var state = new ReadinessState();
        state.Update(new ReadinessSnapshot(
            IsReady: true,
            PostgresOk: true,
            StorageQueueOk: true,
            ToolStoreOk: true,
            LastCheckedAt: DateTime.UtcNow,
            PostgresError: null,
            StorageQueueError: null,
            ToolStoreError: null));

        state.MarkNotReady("shutdown initiated");

        var current = state.Current;
        current.IsReady.Should().BeFalse();
        current.PostgresError.Should().Be("shutdown initiated");
    }

    [Fact]
    public void MarkNotReady_DoesNotAffectLastCheckedAt()
    {
        var state = new ReadinessState();
        var priorCheck = DateTime.UtcNow.AddSeconds(-30);
        state.Update(new ReadinessSnapshot(
            IsReady: true,
            PostgresOk: true,
            StorageQueueOk: true,
            ToolStoreOk: true,
            LastCheckedAt: priorCheck,
            PostgresError: null,
            StorageQueueError: null,
            ToolStoreError: null));

        state.MarkNotReady("draining");

        state.Current.LastCheckedAt.Should().Be(priorCheck);
    }
}
