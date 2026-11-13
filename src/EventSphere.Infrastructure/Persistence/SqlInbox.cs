using EventSphere.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventSphere.Infrastructure.Persistence;

public sealed class SqlInbox(WriteDbContext database, IClock clock) : IInbox
{
    public async Task<bool> ExecuteOnceAsync(IntegrationMessage message, Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        if (await database.Inbox.AsNoTracking().AnyAsync(x => x.MessageId == message.MessageId, cancellationToken))
            return false;

        IDbContextTransaction? transaction = null;
        if (database.Database.IsRelational()) transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            await handler(cancellationToken);
            database.Inbox.Add(new InboxRecord
            {
                MessageId = message.MessageId,
                MessageType = message.MessageType,
                ProcessedAt = clock.UtcNow
            });
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                return false;
            }
        }
    }
}
