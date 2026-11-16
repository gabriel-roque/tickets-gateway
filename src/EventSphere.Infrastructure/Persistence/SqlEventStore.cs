using EventSphere.Application;
using EventSphere.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventSphere.Infrastructure.Persistence;

public sealed class SqlEventStore(WriteDbContext database) : IEventStore
{
    public async Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(string streamId, CancellationToken cancellationToken)
    {
        var records = await database.Events.AsNoTracking()
            .Where(x => x.StreamId == streamId)
            .OrderBy(x => x.StreamVersion)
            .ToListAsync(cancellationToken);
        return records.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<StoredEvent>> ReadAllAsync(
        long afterPosition, int batchSize, CancellationToken cancellationToken)
    {
        var records = await database.Events.AsNoTracking()
            .Where(x => x.GlobalPosition > afterPosition)
            .OrderBy(x => x.GlobalPosition)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        return records.Select(Map).ToArray();
    }

    public async Task AppendAsync(string streamId, long expectedVersion, IReadOnlyCollection<IDomainEvent> events,
        EventMetadata metadata, CancellationToken cancellationToken)
        => await AppendManyAsync([new EventStreamAppend(streamId, expectedVersion, events, metadata)], cancellationToken);

    public async Task AppendManyAsync(IReadOnlyCollection<EventStreamAppend> appends,
        CancellationToken cancellationToken)
    {
        var pending = appends.Where(x => x.Events.Count > 0).ToArray();
        if (pending.Length == 0) return;

        IDbContextTransaction? ownedTransaction = null;
        var actualVersions = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            if (database.Database.IsSqlServer())
            {
                if (database.Database.CurrentTransaction is null)
                    ownedTransaction = await database.Database.BeginTransactionAsync(cancellationToken);

                // IDENTITY values are allocated before commit. Without commit serialization,
                // a projector can observe position N+1, advance its checkpoint, and permanently
                // skip N when the older transaction commits later. A transaction-owned SQL
                // application lock keeps the append/commit order identical to GlobalPosition.
                await database.Database.ExecuteSqlRawAsync("""
                    DECLARE @result int;
                    EXEC @result = sys.sp_getapplock
                        @Resource = N'eventsphere:event-position',
                        @LockMode = N'Exclusive',
                        @LockOwner = N'Transaction',
                        @LockTimeout = 30000;
                    IF @result < 0 THROW 51000, 'Could not acquire EventSphere event-position lock.', 1;
                    """, cancellationToken);
            }

            foreach (var append in pending)
            {
                var actualVersion = await database.Events
                    .Where(x => x.StreamId == append.StreamId)
                    .Select(x => (long?)x.StreamVersion)
                    .MaxAsync(cancellationToken) ?? -1;
                actualVersions[append.StreamId] = actualVersion;
                if (actualVersion != append.ExpectedVersion)
                    throw new ExpectedVersionException(append.StreamId, append.ExpectedVersion, actualVersion);

                var version = append.ExpectedVersion;
                foreach (var @event in append.Events)
                {
                    version++;
                    var payload = EventSerializer.Serialize(@event);
                    database.Events.Add(new EventRecord
                    {
                        EventId = @event.EventId,
                        StreamId = append.StreamId,
                        StreamVersion = version,
                        EventType = @event.GetType().Name,
                        SchemaVersion = @event.SchemaVersion,
                        Payload = payload,
                        Metadata = EventSerializer.SerializeMetadata(append.Metadata),
                        OccurredAt = @event.OccurredAt
                    });
                    database.Outbox.Add(new OutboxRecord
                    {
                        MessageId = @event.EventId,
                        MessageType = @event.GetType().Name,
                        Payload = payload,
                        PartitionKey = append.StreamId,
                        CorrelationId = append.Metadata.CorrelationId,
                        CausationId = append.Metadata.CausationId,
                        OccurredAt = @event.OccurredAt,
                        NextAttemptAt = @event.OccurredAt
                    });
                }
            }

            await database.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            if (ownedTransaction is not null)
                await ownedTransaction.RollbackAsync(cancellationToken);
            database.ChangeTracker.Clear();
            var first = pending[0];
            throw new ExpectedVersionException(first.StreamId, first.ExpectedVersion,
                actualVersions[first.StreamId] + 1) { Source = exception.Source };
        }
        catch
        {
            if (ownedTransaction is not null)
                await ownedTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }

    private static StoredEvent Map(EventRecord record) => new(
        record.GlobalPosition,
        record.StreamId,
        record.StreamVersion,
        EventSerializer.Deserialize(record.EventType, record.SchemaVersion, record.Payload),
        EventSerializer.DeserializeMetadata(record.Metadata));
}
