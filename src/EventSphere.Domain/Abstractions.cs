namespace EventSphere.Domain;

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
    int SchemaVersion { get; }
}

public abstract record DomainEvent(Guid EventId, DateTimeOffset OccurredAt) : IDomainEvent
{
    public virtual int SchemaVersion => 1;
}

public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _uncommitted = [];

    public Guid Id { get; protected set; }
    public long Version { get; private set; } = -1;
    public long ExpectedVersion => Version - _uncommitted.Count;
    public IReadOnlyCollection<IDomainEvent> UncommittedEvents => _uncommitted.AsReadOnly();

    protected void Raise(IDomainEvent @event)
    {
        Apply(@event);
        _uncommitted.Add(@event);
        Version++;
    }

    public void LoadFromHistory(IEnumerable<IDomainEvent> history)
    {
        foreach (var @event in history)
        {
            Apply(@event);
            Version++;
        }
    }

    public void MarkChangesAsCommitted() => _uncommitted.Clear();

    protected abstract void Apply(IDomainEvent @event);
}

public sealed class DomainRuleViolationException(string message) : InvalidOperationException(message);

public sealed class AggregateNotFoundException(string streamId)
    : InvalidOperationException($"Aggregate stream '{streamId}' was not found.");

public sealed class ExpectedVersionException(string streamId, long expectedVersion, long actualVersion)
    : InvalidOperationException($"Concurrency conflict on '{streamId}': expected {expectedVersion}, actual {actualVersion}.")
{
    public string StreamId { get; } = streamId;
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}

public sealed class IdempotencyConflictException(string message) : InvalidOperationException(message);
