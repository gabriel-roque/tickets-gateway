using EventSphere.Domain;

namespace EventSphere.Application;

public sealed class AggregateRepository(IEventStore eventStore)
{
    public async Task<T> LoadAsync<T>(string streamId, CancellationToken cancellationToken)
        where T : AggregateRoot, new()
    {
        var history = await eventStore.ReadStreamAsync(streamId, cancellationToken);
        if (history.Count == 0) throw new AggregateNotFoundException(streamId);
        var aggregate = new T();
        aggregate.LoadFromHistory(history.Select(x => x.Data));
        return aggregate;
    }

    public async Task SaveAsync<T>(string streamId, T aggregate, EventMetadata metadata,
        CancellationToken cancellationToken) where T : AggregateRoot
    {
        var changes = aggregate.UncommittedEvents.ToArray();
        if (changes.Length == 0) return;
        await eventStore.AppendAsync(streamId, aggregate.ExpectedVersion, changes, metadata, cancellationToken);
        aggregate.MarkChangesAsCommitted();
    }
}
