using EventSphere.Application;
using EventSphere.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventSphere.Infrastructure.Persistence;

public sealed class SqlSagaRepository(WriteDbContext database, IClock clock) : ISagaRepository
{
    public async Task<PurchaseSagaState?> GetAsync(Guid sagaId, CancellationToken cancellationToken)
    {
        var record = await database.Sagas.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sagaId, cancellationToken);
        return record is null ? null : SagaMapper.ToModel(record);
    }

    public async Task<PurchaseSagaState?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var record = await database.Sagas.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);
        return record is null ? null : SagaMapper.ToModel(record);
    }

    public async Task<IReadOnlyList<PurchaseSagaState>> ClaimRunnableAsync(
        int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        IDbContextTransaction? transaction = null;
        if (database.Database.IsSqlServer())
            transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            var candidates = database.Database.IsSqlServer()
                ? await database.Sagas.FromSqlInterpolated($"""
                    SELECT TOP ({batchSize}) * FROM [Sagas] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE [Status] IN (0, 2) AND [NextAttemptAt] <= {now}
                      AND ([LeaseUntil] IS NULL OR [LeaseUntil] < {now})
                    ORDER BY [NextAttemptAt]
                    """).ToListAsync(cancellationToken)
                : await database.Sagas
                    .Where(x => (x.Status == (int)SagaStatus.Running || x.Status == (int)SagaStatus.Compensating)
                        && x.NextAttemptAt <= now && (x.LeaseUntil == null || x.LeaseUntil < now))
                    .OrderBy(x => x.NextAttemptAt)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);
            foreach (var candidate in candidates)
            {
                candidate.LeaseUntil = now.Add(lease);
                candidate.Version++;
            }
            if (candidates.Count > 0) await database.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return candidates.Select(SagaMapper.ToModel).ToArray();
        }
    }

    public async Task SaveAsync(PurchaseSagaState saga, long expectedVersion, CancellationToken cancellationToken)
    {
        var record = SagaMapper.ToRecord(saga);
        record.Version = expectedVersion + 1;
        foreach (var tracked in database.ChangeTracker.Entries<SagaRecord>()
                     .Where(x => x.Entity.Id == record.Id).ToArray())
            tracked.State = EntityState.Detached;
        database.Attach(record);
        database.Entry(record).State = EntityState.Modified;
        database.Entry(record).Property(x => x.Version).OriginalValue = expectedVersion;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            saga.Version = record.Version;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ExpectedVersionException($"saga-{saga.Id:N}", expectedVersion, expectedVersion + 1);
        }
    }
}
