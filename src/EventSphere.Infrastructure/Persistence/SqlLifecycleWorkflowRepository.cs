using EventSphere.Application;
using EventSphere.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventSphere.Infrastructure.Persistence;

public sealed class SqlLifecycleWorkflowRepository(WriteDbContext database, IClock clock)
    : ILifecycleWorkflowRepository
{
    public async Task<LifecycleWorkflowState?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await database.LifecycleWorkflows.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return record is null ? null : Map(record);
    }

    public async Task CreateAsync(LifecycleWorkflowState workflow, CancellationToken cancellationToken)
    {
        var existing = await database.LifecycleWorkflows.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == workflow.Id, cancellationToken);
        if (existing is not null) return;
        database.LifecycleWorkflows.Add(Map(workflow));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or ArgumentException)
        {
            database.ChangeTracker.Clear();
            if (await database.LifecycleWorkflows.AsNoTracking()
                .AnyAsync(x => x.Id == workflow.Id, cancellationToken)) return;
            throw;
        }
    }

    public async Task SaveAsync(LifecycleWorkflowState workflow, long expectedVersion, CancellationToken cancellationToken)
    {
        var record = Map(workflow);
        record.Version = expectedVersion + 1;
        foreach (var tracked in database.ChangeTracker.Entries<LifecycleWorkflowRecord>()
                     .Where(x => x.Entity.Id == record.Id).ToArray())
            tracked.State = EntityState.Detached;
        database.Attach(record);
        database.Entry(record).State = EntityState.Modified;
        database.Entry(record).Property(x => x.Version).OriginalValue = expectedVersion;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            workflow.Version = record.Version;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ExpectedVersionException($"workflow-{workflow.Id:N}", expectedVersion, expectedVersion + 1);
        }
    }

    public async Task<IReadOnlyList<LifecycleWorkflowState>> ClaimRunnableAsync(
        int batchSize, TimeSpan lease, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        IDbContextTransaction? transaction = null;
        if (database.Database.IsSqlServer())
            transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            var records = database.Database.IsSqlServer()
                ? await database.LifecycleWorkflows.FromSqlInterpolated($"""
                    SELECT TOP ({batchSize}) * FROM [LifecycleWorkflows] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE [Status] = 0 AND [NextAttemptAt] <= {now}
                      AND ([LeaseUntil] IS NULL OR [LeaseUntil] < {now})
                    ORDER BY [NextAttemptAt]
                    """).ToListAsync(cancellationToken)
                : await database.LifecycleWorkflows
                    .Where(x => x.Status == (int)LifecycleWorkflowStatus.Running && x.NextAttemptAt <= now
                        && (x.LeaseUntil == null || x.LeaseUntil < now))
                    .OrderBy(x => x.NextAttemptAt)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);
            foreach (var record in records)
            {
                record.LeaseUntil = now.Add(lease);
                record.Version++;
            }
            if (records.Count > 0) await database.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return records.Select(Map).ToArray();
        }
    }

    private static LifecycleWorkflowRecord Map(LifecycleWorkflowState state) => new()
    {
        Id = state.Id,
        Type = (int)state.Type,
        AggregateId = state.AggregateId,
        InitiatorId = state.InitiatorId,
        Payload = state.Payload,
        Step = state.Step,
        Status = (int)state.Status,
        Attempts = state.Attempts,
        LastError = state.LastError,
        NextAttemptAt = state.NextAttemptAt,
        LeaseUntil = state.LeaseUntil,
        CreatedAt = state.CreatedAt,
        UpdatedAt = state.UpdatedAt,
        Version = state.Version
    };

    private static LifecycleWorkflowState Map(LifecycleWorkflowRecord record) => new()
    {
        Id = record.Id,
        Type = (LifecycleWorkflowType)record.Type,
        AggregateId = record.AggregateId,
        InitiatorId = record.InitiatorId,
        Payload = record.Payload,
        Step = record.Step,
        Status = (LifecycleWorkflowStatus)record.Status,
        Attempts = record.Attempts,
        LastError = record.LastError,
        NextAttemptAt = record.NextAttemptAt,
        LeaseUntil = record.LeaseUntil,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
        Version = record.Version
    };
}
