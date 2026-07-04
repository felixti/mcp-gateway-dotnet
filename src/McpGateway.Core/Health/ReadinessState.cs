namespace McpGateway.Core.Health;

public class ReadinessState : IReadinessState
{
    private SnapshotBox _current = new(new ReadinessSnapshot(
        IsReady: false,
        PostgresOk: false,
        StorageQueueOk: false,
        ToolStoreOk: false,
        LastCheckedAt: null,
        PostgresError: null,
        StorageQueueError: null,
        ToolStoreError: null));

    public ReadinessSnapshot Current => Volatile.Read(ref _current).Value;

    public void Update(ReadinessSnapshot snapshot)
    {
        Volatile.Write(ref _current, new SnapshotBox(snapshot));
    }

    public void MarkNotReady(string reason)
    {
        var current = Volatile.Read(ref _current).Value;
        var next = current with
        {
            IsReady = false,
            PostgresError = reason ?? current.PostgresError,
            StorageQueueError = reason ?? current.StorageQueueError,
            ToolStoreError = reason ?? current.ToolStoreError
        };
        Volatile.Write(ref _current, new SnapshotBox(next));
    }

    private sealed class SnapshotBox
    {
        public SnapshotBox(ReadinessSnapshot value) { Value = value; }
        public ReadinessSnapshot Value { get; }
    }
}
