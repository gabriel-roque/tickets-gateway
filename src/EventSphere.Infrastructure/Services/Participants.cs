using EventSphere.Application;
using EventSphere.Domain;
using EventSphere.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace EventSphere.Infrastructure.Services;

public sealed class ReservationOptions
{
    public const string SectionName = "Reservations";
    public int TtlMinutes { get; init; } = 5;
}

public sealed class InventoryParticipant(
    AggregateRepository aggregates,
    WriteDbContext database,
    IClock clock,
    IOptions<ReservationOptions> options) : IInventoryParticipant
{
    private readonly TimeSpan reservationTtl = TimeSpan.FromMinutes(options.Value.TtlMinutes);

    public async Task ReserveAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        var expiresAt = clock.UtcNow.Add(reservationTtl);
        var inventory = await aggregates.LoadAsync<InventoryAggregate>(Streams.Inventory(saga.InventoryId), cancellationToken);
        inventory.Reserve(saga.ReservationId, saga.OrderId, saga.Quantity, expiresAt, clock.UtcNow);
        await aggregates.SaveAsync(Streams.Inventory(saga.InventoryId), inventory, Metadata(saga), cancellationToken);
        var reservation = await database.InventoryReservations.SingleOrDefaultAsync(
            x => x.ReservationId == saga.ReservationId, cancellationToken);
        if (reservation is null)
            database.InventoryReservations.Add(new InventoryReservationRecord
            {
                ReservationId = saga.ReservationId,
                InventoryId = saga.InventoryId,
                OrderId = saga.OrderId,
                ExpiresAt = expiresAt,
                Status = "Active"
            });
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task ConfirmAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        var inventory = await aggregates.LoadAsync<InventoryAggregate>(Streams.Inventory(saga.InventoryId), cancellationToken);
        inventory.Confirm(saga.ReservationId, clock.UtcNow);
        await aggregates.SaveAsync(Streams.Inventory(saga.InventoryId), inventory, Metadata(saga), cancellationToken);
        await MarkReservationAsync(saga.ReservationId, "Confirmed", cancellationToken);
    }

    public async Task ReleaseAsync(PurchaseSagaState saga, string reason, CancellationToken cancellationToken)
    {
        var inventory = await aggregates.LoadAsync<InventoryAggregate>(Streams.Inventory(saga.InventoryId), cancellationToken);
        inventory.Release(saga.ReservationId, reason, clock.UtcNow);
        await aggregates.SaveAsync(Streams.Inventory(saga.InventoryId), inventory, Metadata(saga), cancellationToken);
        await MarkReservationAsync(saga.ReservationId, "Released", cancellationToken);
    }

    public async Task ReturnAsync(PurchaseSagaState saga, string reason, CancellationToken cancellationToken)
    {
        var inventory = await aggregates.LoadAsync<InventoryAggregate>(Streams.Inventory(saga.InventoryId), cancellationToken);
        inventory.ReturnConfirmed(saga.ReservationId, reason, clock.UtcNow);
        await aggregates.SaveAsync(Streams.Inventory(saga.InventoryId), inventory, Metadata(saga), cancellationToken);
        await MarkReservationAsync(saga.ReservationId, "Returned", cancellationToken);
    }

    public async Task<int> ExpireDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        var due = await database.InventoryReservations
            .Where(x => x.Status == "Active" && x.ExpiresAt <= clock.UtcNow)
            .OrderBy(x => x.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (var record in due)
        {
            var inventory = await aggregates.LoadAsync<InventoryAggregate>(Streams.Inventory(record.InventoryId), cancellationToken);
            inventory.Expire(record.ReservationId, clock.UtcNow);
            await aggregates.SaveAsync(Streams.Inventory(record.InventoryId), inventory,
                new EventMetadata(record.ReservationId, record.ReservationId, "reservation-expiry", string.Empty),
                cancellationToken);
            record.Status = "Expired";
            await database.SaveChangesAsync(cancellationToken);
        }
        return due.Count;
    }

    private async Task MarkReservationAsync(Guid reservationId, string status, CancellationToken cancellationToken)
    {
        var record = await database.InventoryReservations.SingleOrDefaultAsync(
            x => x.ReservationId == reservationId, cancellationToken);
        if (record is null) return;
        record.Status = status;
        await database.SaveChangesAsync(cancellationToken);
    }

    private static EventMetadata Metadata(PurchaseSagaState saga) =>
        new(saga.Id, saga.Id, $"saga:{saga.Id:N}", string.Empty);
}

public sealed class PricingParticipant(WriteDbContext database) : IPricingParticipant
{
    public async Task LockAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        var existing = await database.PricingLocks.SingleOrDefaultAsync(x => x.SagaId == saga.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.Amount != saga.UnitPrice.Amount || existing.Currency != saga.UnitPrice.Currency)
                throw new DomainRuleViolationException("Pricing lock idempotency conflict.");
            existing.Active = true;
        }
        else
        {
            database.PricingLocks.Add(new PricingLockRecord
            {
                SagaId = saga.Id,
                OrderId = saga.OrderId,
                Amount = saga.UnitPrice.Amount,
                Currency = saga.UnitPrice.Currency,
                Active = true
            });
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        var value = await database.PricingLocks.SingleOrDefaultAsync(x => x.SagaId == saga.Id, cancellationToken);
        if (value is null || !value.Active) return;
        value.Active = false;
        await database.SaveChangesAsync(cancellationToken);
    }
}

public sealed class FraudParticipant : IFraudParticipant
{
    public Task ApproveAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (saga.UnitPrice.Amount * saga.Quantity > 50_000m)
            throw new DomainRuleViolationException("Order requires manual fraud review.");
        return Task.CompletedTask;
    }
}

public sealed class PaymentParticipant(WriteDbContext database, IClock clock) : IPaymentParticipant
{
    public async Task AuthorizeAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        if (await database.Set<FaultInjectionRecord>().AsNoTracking()
            .AnyAsync(x => x.Key == "payment" && x.Until > clock.UtcNow, cancellationToken))
            throw new HttpRequestException("Simulated payment provider outage.");
        var existing = await database.Payments.SingleOrDefaultAsync(x => x.OrderId == saga.OrderId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status is "Authorized" or "Refunded") return;
            if (existing.Status == "Cancelled") throw new DomainRuleViolationException("A cancelled payment cannot be authorized again.");
        }
        else
        {
            database.Payments.Add(new PaymentRecord
            {
                Id = Guid.NewGuid(),
                OrderId = saga.OrderId,
                Amount = saga.UnitPrice.Amount * saga.Quantity,
                Currency = saga.UnitPrice.Currency,
                Status = "Authorized",
                ExternalId = saga.OrderId.ToString("N"),
                UpdatedAt = clock.UtcNow
            });
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        var payment = await database.Payments.SingleOrDefaultAsync(x => x.OrderId == saga.OrderId, cancellationToken);
        if (payment is null || payment.Status == "Cancelled") return;
        if (payment.Status == "Refunded") throw new DomainRuleViolationException("Refunded payment cannot be cancelled.");
        payment.Status = "Cancelled";
        payment.UpdatedAt = clock.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RefundAsync(Guid orderId, Money amount, CancellationToken cancellationToken)
    {
        var payment = await database.Payments.SingleAsync(x => x.OrderId == orderId, cancellationToken);
        if (payment.Status == "Refunded") return;
        if (payment.Status != "Authorized") throw new DomainRuleViolationException("Only authorized payments can be refunded.");
        if (payment.Amount != amount.Amount || payment.Currency != amount.Currency)
            throw new DomainRuleViolationException("Refund amount differs from the authorized payment.");
        payment.Status = "Refunded";
        payment.UpdatedAt = clock.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }
}

public sealed class TicketParticipant(
    WriteDbContext database,
    IEventStore eventStore,
    AggregateRepository aggregates,
    IClock clock) : ITicketParticipant
{
    public async Task<Guid> IssueAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        var existing = await database.TicketIndex.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == saga.OrderId, cancellationToken);
        if (existing is not null) return existing.TicketId;

        var ticketId = Guid.NewGuid();
        var ticket = TicketAggregate.Issue(ticketId, saga.OrderId, saga.CustomerId,
            $"qr-{Guid.NewGuid():N}", clock.UtcNow);
        IDbContextTransaction? transaction = null;
        if (database.Database.IsRelational()) transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction)
        {
            await eventStore.AppendAsync(Streams.Ticket(ticketId), -1, ticket.UncommittedEvents,
                new EventMetadata(saga.Id, saga.Id, $"saga:{saga.Id:N}", string.Empty), cancellationToken);
            database.TicketIndex.Add(new TicketIndexRecord { TicketId = ticketId, OrderId = saga.OrderId });
            await database.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        ticket.MarkChangesAsCommitted();
        return ticketId;
    }

    public async Task InvalidateAsync(Guid ticketId, string reason, CancellationToken cancellationToken)
    {
        var ticket = await aggregates.LoadAsync<TicketAggregate>(Streams.Ticket(ticketId), cancellationToken);
        ticket.Invalidate(reason, clock.UtcNow);
        await aggregates.SaveAsync(Streams.Ticket(ticketId), ticket,
            new EventMetadata(ticketId, ticketId, "ticketing", string.Empty), cancellationToken);
    }
}

public sealed class SettlementParticipant(WriteDbContext database) : ISettlementParticipant
{
    public async Task RecordSaleAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        var existing = await database.Settlements.SingleOrDefaultAsync(x => x.OrderId == saga.OrderId, cancellationToken);
        if (existing is not null)
        {
            existing.Status = "Pending";
        }
        else
        {
            var gross = saga.UnitPrice.Amount * saga.Quantity;
            var fee = decimal.Round(gross * 0.10m, 2, MidpointRounding.ToEven);
            database.Settlements.Add(new SettlementRecord
            {
                OrderId = saga.OrderId,
                GrossAmount = gross,
                PlatformFee = fee,
                OrganizerAmount = gross - fee,
                Currency = saga.UnitPrice.Currency,
                Status = "Pending"
            });
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task ReverseSaleAsync(PurchaseSagaState saga, CancellationToken cancellationToken)
    {
        var settlement = await database.Settlements.SingleOrDefaultAsync(x => x.OrderId == saga.OrderId, cancellationToken);
        if (settlement is null || settlement.Status == "Reversed") return;
        settlement.Status = "Reversed";
        await database.SaveChangesAsync(cancellationToken);
    }
}

public sealed class NotificationParticipant(WriteDbContext database, IClock clock) : INotificationParticipant
{
    public Task NotifyOrderCompletedAsync(PurchaseSagaState saga, CancellationToken cancellationToken) =>
        NotifyAsync(saga.OrderId, "order-completed", cancellationToken);

    public async Task NotifyAsync(Guid orderId, string template, CancellationToken cancellationToken)
    {
        if (await database.Notifications.AnyAsync(x => x.OrderId == orderId && x.Template == template, cancellationToken))
            return;
        database.Notifications.Add(new NotificationRecord
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Template = template,
            Status = "Pending",
            CreatedAt = clock.UtcNow
        });
        await database.SaveChangesAsync(cancellationToken);
    }
}
