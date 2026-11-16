using System.Data;
using EventSphere.Application;
using EventSphere.Domain;
using EventSphere.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace EventSphere.Infrastructure.Services;

public sealed class SeatLockOptions
{
    public const string SectionName = "SeatLocks";
    public int TtlSeconds { get; init; } = 120;
}

public sealed class SqlSeatLockService(
    WriteDbContext database,
    IClock clock,
    IOptions<SeatLockOptions> options) : ISeatLockService
{
    private readonly TimeSpan lockTtl = TimeSpan.FromSeconds(options.Value.TtlSeconds);

    public async Task LockAsync(LockSeatCommand command, CancellationToken cancellationToken)
    {
        _ = Seat.Create(command.Sector, command.Row, command.Number);
        if (command.OwnerId == Guid.Empty || command.ShowId == Guid.Empty || command.ReservationId == Guid.Empty)
            throw new DomainRuleViolationException("Owner, show and reservation IDs are required.");
        var expiresAt = clock.UtcNow.Add(lockTtl);

        IDbContextTransaction? transaction = null;
        if (database.Database.IsRelational())
            transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using (transaction)
        {
            SeatLockRecord? current;
            if (database.Database.IsSqlServer())
            {
                current = await database.SeatLocks.FromSqlInterpolated($@"
                    SELECT * FROM SeatLocks WITH (UPDLOCK, ROWLOCK)
                    WHERE ShowId = {command.ShowId} AND Sector = {command.Sector}
                      AND Row = {command.Row} AND Number = {command.Number}")
                    .SingleOrDefaultAsync(cancellationToken);
            }
            else
            {
                current = await database.SeatLocks.SingleOrDefaultAsync(x => x.ShowId == command.ShowId
                    && x.Sector == command.Sector && x.Row == command.Row && x.Number == command.Number,
                    cancellationToken);
            }

            if (current is not null && current.ExpiresAt <= clock.UtcNow)
            {
                current.OwnerId = command.OwnerId;
                current.ReservationId = command.ReservationId;
                current.ExpiresAt = expiresAt;
                await database.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return;
            }
            if (current is not null)
            {
                if (current.ReservationId != command.ReservationId || current.OwnerId != command.OwnerId)
                    throw new DomainRuleViolationException("Seat is already locked by another reservation.");
                return;
            }

            database.SeatLocks.Add(new SeatLockRecord
            {
                ShowId = command.ShowId,
                OwnerId = command.OwnerId,
                Sector = command.Sector,
                Row = command.Row,
                Number = command.Number,
                ReservationId = command.ReservationId,
                ExpiresAt = expiresAt
            });
            await database.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
    }

    public async Task ReleaseAsync(Guid reservationId, Guid actorId, bool isAdmin,
        CancellationToken cancellationToken)
    {
        var current = await database.SeatLocks.SingleOrDefaultAsync(x => x.ReservationId == reservationId, cancellationToken);
        if (current is null) return;
        if (!isAdmin && current.OwnerId != actorId)
            throw new DomainRuleViolationException("Only the lock owner can release this seat.");
        database.SeatLocks.Remove(current);
        await database.SaveChangesAsync(cancellationToken);
    }
}
