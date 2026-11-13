using EventSphere.Application;
using EventSphere.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventSphere.Infrastructure.Workers;

public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IMessagePublisher publisher,
    IClock clock,
    ILogger<OutboxDispatcher> logger)
{
    public async Task<int> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        List<OutboxRecord> messages;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<WriteDbContext>();
            var now = clock.UtcNow;
            IDbContextTransaction? transaction = null;
            if (database.Database.IsSqlServer())
                transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await using (transaction)
            {
                messages = database.Database.IsSqlServer()
                    ? await database.Outbox.FromSqlInterpolated($"""
                        SELECT TOP ({batchSize}) * FROM [Outbox] WITH (UPDLOCK, READPAST, ROWLOCK)
                        WHERE [PublishedAt] IS NULL AND [DeadLetteredAt] IS NULL
                          AND [NextAttemptAt] <= {now}
                          AND ([LeaseUntil] IS NULL OR [LeaseUntil] < {now})
                        ORDER BY [OccurredAt]
                        """).ToListAsync(cancellationToken)
                    : await database.Outbox
                        .Where(x => x.PublishedAt == null && x.DeadLetteredAt == null
                            && x.NextAttemptAt <= now && (x.LeaseUntil == null || x.LeaseUntil < now))
                        .OrderBy(x => x.OccurredAt)
                        .Take(batchSize)
                        .ToListAsync(cancellationToken);
                foreach (var message in messages) message.LeaseUntil = now.AddSeconds(30);
                if (messages.Count > 0) await database.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            }
        }

        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(new IntegrationMessage(
                    message.MessageId, message.MessageType, message.Payload, message.PartitionKey,
                    message.CorrelationId, message.CausationId, message.OccurredAt), cancellationToken);
                await MarkPublishedAsync(message.MessageId, null, cancellationToken);
                EventSphereTelemetry.OutboxPublished.Add(1);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Failed to publish outbox message {MessageId}", message.MessageId);
                EventSphereTelemetry.OutboxFailures.Add(1);
                await MarkPublishedAsync(message.MessageId, exception.Message, cancellationToken);
            }
        }
        return messages.Count;
    }

    private async Task MarkPublishedAsync(Guid id, string? error, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<WriteDbContext>();
        var message = await database.Outbox.SingleAsync(x => x.MessageId == id, cancellationToken);
        message.Attempts++;
        message.LastError = error;
        message.LeaseUntil = null;
        if (error is null) message.PublishedAt = clock.UtcNow;
        else if (message.Attempts >= 10) message.DeadLetteredAt = clock.UtcNow;
        else message.NextAttemptAt = clock.UtcNow.AddSeconds(Math.Pow(2, Math.Min(message.Attempts, 8)));
        await database.SaveChangesAsync(cancellationToken);
    }
}
