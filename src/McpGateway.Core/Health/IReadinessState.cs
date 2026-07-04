namespace McpGateway.Core.Health;

public interface IReadinessState
{
    ReadinessSnapshot Current { get; }
    void Update(ReadinessSnapshot snapshot);
    void MarkNotReady(string reason);
}
