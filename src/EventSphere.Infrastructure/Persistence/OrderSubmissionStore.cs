using EventSphere.Application;
using EventSphere.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EventSphere.Infrastructure.Persistence;

public sealed class OrderSubmissionStore(
    WriteDbContext database,
    IEventStore eventStore) : IOrderSubmissionStore, IIdempotencyStore
{
    public async Task<OrderSubmissionResult?> FindOrderAsync(string key, CancellationToken cancellationToken) =>
        await database.Idempotency.AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => new OrderSubmissionResult(x.OrderId, x.SagaId, x.Fingerprint))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<OrderSubmissionResult?> FindLegacyOrderAsync(string rawKey, Guid customerId,
        CancellationToken cancellationToken)
    {
        var legacy = await (from idempotency in database.Idempotency.AsNoTracking()
                            join saga in database.Sagas.AsNoTracking() on idempotency.SagaId equals saga.Id
                            where idempotency.Key == rawKey && idempotency.RawKey == rawKey
                                && idempotency.Fingerprint == string.Empty && saga.CustomerId == customerId
                            select new
                            {
                                idempotency.OrderId,
                                idempotency.SagaId,
                                saga.CustomerId,
                                saga.InventoryId,
                                saga.Quantity
                            }).SingleOrDefaultAsync(cancellationToken);
        return legacy is null ? null : new OrderSubmissionResult(legacy.OrderId, legacy.SagaId,
            OrderIdempotency.Fingerprint(new CreateOrderCommand(
                legacy.CustomerId, legacy.InventoryId, legacy.Quantity, rawKey)));
    }

    public async Task<OrderSubmissionResult> SubmitAsync(OrderAggregate order, PurchaseSagaState saga,
        string rawIdempotencyKey, string fingerprint, EventMetadata metadata, CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (database.Database.IsRelational())
            transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        await using (transaction)
        {
            await eventStore.AppendAsync(Streams.Order(order.Id), -1, order.UncommittedEvents, metadata, cancellationToken);
            database.Sagas.Add(SagaMapper.ToRecord(saga));
            database.Idempotency.Add(new IdempotencyRecord
            {
                Key = order.IdempotencyKey,
                RawKey = rawIdempotencyKey,
                Fingerprint = fingerprint,
                OrderId = order.Id,
                SagaId = saga.Id,
                CreatedAt = saga.CreatedAt
            });
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is DbUpdateException or ArgumentException)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                database.ChangeTracker.Clear();
                var existing = await FindOrderAsync(order.IdempotencyKey, cancellationToken);
                if (existing is not null)
                {
                    if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                        throw new IdempotencyConflictException(
                            "The idempotency key was concurrently used with a different request.");
                    return existing;
                }
                throw;
            }
        }
        order.MarkChangesAsCommitted();
        return new OrderSubmissionResult(order.Id, saga.Id, fingerprint);
    }
}

internal static class SagaMapper
{
    public static SagaRecord ToRecord(PurchaseSagaState saga) => new()
    {
        Id = saga.Id,
        OrderId = saga.OrderId,
        CustomerId = saga.CustomerId,
        InventoryId = saga.InventoryId,
        ReservationId = saga.ReservationId,
        Quantity = saga.Quantity,
        UnitPrice = saga.UnitPrice.Amount,
        Currency = saga.UnitPrice.Currency,
        TicketId = saga.TicketId,
        Step = (int)saga.Step,
        Status = (int)saga.Status,
        Attempts = saga.Attempts,
        LastError = saga.LastError,
        CreatedAt = saga.CreatedAt,
        UpdatedAt = saga.UpdatedAt,
        NextAttemptAt = saga.NextAttemptAt,
        LeaseUntil = saga.LeaseUntil,
        Version = saga.Version
    };

    public static PurchaseSagaState ToModel(SagaRecord record) => new()
    {
        Id = record.Id,
        OrderId = record.OrderId,
        CustomerId = record.CustomerId,
        InventoryId = record.InventoryId,
        ReservationId = record.ReservationId,
        Quantity = record.Quantity,
        UnitPrice = Money.Create(record.UnitPrice, record.Currency),
        TicketId = record.TicketId,
        Step = (PurchaseStep)record.Step,
        Status = (SagaStatus)record.Status,
        Attempts = record.Attempts,
        LastError = record.LastError,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
        NextAttemptAt = record.NextAttemptAt,
        LeaseUntil = record.LeaseUntil,
        Version = record.Version
    };
}
