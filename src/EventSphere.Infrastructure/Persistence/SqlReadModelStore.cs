using EventSphere.Application;
using EventSphere.Domain;
using Microsoft.EntityFrameworkCore;

namespace EventSphere.Infrastructure.Persistence;

public sealed class SqlReadModelStore(ReadDbContext database) : IReadModelStore
{
    public Task<OrderReadModel?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        database.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

    public Task<InventoryReadModel?> GetInventoryAsync(Guid inventoryId, CancellationToken cancellationToken) =>
        database.Inventories.AsNoTracking().SingleOrDefaultAsync(x => x.InventoryId == inventoryId, cancellationToken);

    public Task<TicketReadModel?> GetTicketAsync(Guid ticketId, CancellationToken cancellationToken) =>
        database.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.TicketId == ticketId, cancellationToken);

    public async Task<long> GetCheckpointAsync(string projector, CancellationToken cancellationToken) =>
        await database.Checkpoints.AsNoTracking().Where(x => x.Projector == projector)
            .Select(x => (long?)x.Position).SingleOrDefaultAsync(cancellationToken) ?? 0;

    public Task ProjectAsync(string projector, StoredEvent @event, CancellationToken cancellationToken) =>
        ProjectBatchAsync(projector, [@event], cancellationToken);

    public async Task ProjectBatchAsync(string projector, IReadOnlyList<StoredEvent> events,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0) return;
        var checkpoint = await database.Checkpoints.SingleOrDefaultAsync(x => x.Projector == projector, cancellationToken);
        foreach (var @event in events)
        {
            if (checkpoint is not null && checkpoint.Position >= @event.GlobalPosition) continue;

            switch (@event.Data)
            {
            case OrderCreated e:
                var createdOrder = await GetOrCreateOrderAsync(e.OrderId, cancellationToken);
                createdOrder.CustomerId = e.CustomerId;
                createdOrder.InventoryId = e.InventoryId;
                createdOrder.ReservationId = e.ReservationId;
                createdOrder.Quantity = e.Quantity;
                createdOrder.Total = e.UnitPrice.Amount * e.Quantity;
                createdOrder.Currency = e.UnitPrice.Currency;
                if (createdOrder.LastEventPosition == 0) createdOrder.Status = OrderStatus.Pending.ToString();
                createdOrder.LastEventPosition = Math.Max(createdOrder.LastEventPosition, @event.GlobalPosition);
                break;
            case OrderStepCompleted e:
                var processingOrder = await GetOrCreateOrderAsync(e.OrderId, cancellationToken);
                processingOrder.Status = e.Step;
                processingOrder.LastEventPosition = @event.GlobalPosition;
                break;
            case OrderCompleted e:
                var completedOrder = await GetOrCreateOrderAsync(e.OrderId, cancellationToken);
                completedOrder.Status = OrderStatus.Completed.ToString();
                completedOrder.TicketId = e.TicketId;
                completedOrder.LastEventPosition = @event.GlobalPosition;
                break;
            case OrderCancelled e:
                var cancelledOrder = await GetOrCreateOrderAsync(e.OrderId, cancellationToken);
                cancelledOrder.Status = OrderStatus.Cancelled.ToString();
                cancelledOrder.LastEventPosition = @event.GlobalPosition;
                break;
            case RefundRequested e:
                var refundingOrder = await GetOrCreateOrderAsync(e.OrderId, cancellationToken);
                refundingOrder.Status = OrderStatus.Refunding.ToString();
                refundingOrder.LastEventPosition = @event.GlobalPosition;
                break;
            case OrderRefunded e:
                var refundedOrder = await GetOrCreateOrderAsync(e.OrderId, cancellationToken);
                refundedOrder.Status = OrderStatus.Refunded.ToString();
                refundedOrder.LastEventPosition = @event.GlobalPosition;
                break;
            case InventoryCreated e:
                var createdInventory = await GetOrCreateInventoryAsync(e.InventoryId, cancellationToken);
                createdInventory.ShowId = e.ShowId;
                createdInventory.SectorId = e.SectorId;
                createdInventory.BucketId = e.BucketId;
                createdInventory.Capacity = e.Capacity;
                createdInventory.Available = e.Capacity - createdInventory.Reserved - createdInventory.Confirmed;
                createdInventory.SalesOpen = true;
                createdInventory.LastEventPosition = Math.Max(createdInventory.LastEventPosition, @event.GlobalPosition);
                break;
            case InventoryReserved e:
                var reservedInventory = await GetOrCreateInventoryAsync(e.InventoryId, cancellationToken);
                reservedInventory.Reserved += e.Quantity;
                reservedInventory.Available -= e.Quantity;
                reservedInventory.LastEventPosition = @event.GlobalPosition;
                break;
            case InventoryReleased e:
                var releasedInventory = await GetOrCreateInventoryAsync(e.InventoryId, cancellationToken);
                releasedInventory.Reserved -= e.Quantity;
                releasedInventory.Available += e.Quantity;
                releasedInventory.LastEventPosition = @event.GlobalPosition;
                break;
            case InventoryConfirmed e:
                var confirmedInventory = await GetOrCreateInventoryAsync(e.InventoryId, cancellationToken);
                confirmedInventory.Reserved -= e.Quantity;
                confirmedInventory.Confirmed += e.Quantity;
                confirmedInventory.LastEventPosition = @event.GlobalPosition;
                break;
            case InventoryReturned e:
                var returnedInventory = await GetOrCreateInventoryAsync(e.InventoryId, cancellationToken);
                returnedInventory.Confirmed -= e.Quantity;
                returnedInventory.Available += e.Quantity;
                returnedInventory.LastEventPosition = @event.GlobalPosition;
                break;
            case InventoryReservationExpired e:
                var expiredInventory = await GetOrCreateInventoryAsync(e.InventoryId, cancellationToken);
                expiredInventory.Reserved -= e.Quantity;
                expiredInventory.Available += e.Quantity;
                expiredInventory.LastEventPosition = @event.GlobalPosition;
                break;
            case InventorySalesClosed e:
                var closedInventory = await GetOrCreateInventoryAsync(e.InventoryId, cancellationToken);
                closedInventory.SalesOpen = false;
                closedInventory.LastEventPosition = @event.GlobalPosition;
                break;
            case TicketIssued e:
                var issuedTicket = await GetOrCreateTicketAsync(e.TicketId, cancellationToken);
                issuedTicket.OrderId = e.OrderId;
                issuedTicket.OwnerId = e.OwnerId;
                issuedTicket.QrCode = e.QrCode;
                if (issuedTicket.LastEventPosition == 0) issuedTicket.Status = TicketStatus.Active.ToString();
                issuedTicket.LastEventPosition = Math.Max(issuedTicket.LastEventPosition, @event.GlobalPosition);
                break;
            case TicketTransferred e:
                var transferredTicket = await GetOrCreateTicketAsync(e.TicketId, cancellationToken);
                transferredTicket.OwnerId = e.NewOwnerId;
                transferredTicket.QrCode = e.NewQrCode;
                transferredTicket.LastEventPosition = @event.GlobalPosition;
                break;
            case TicketCheckedIn e:
                var checkedInTicket = await GetOrCreateTicketAsync(e.TicketId, cancellationToken);
                checkedInTicket.Status = TicketStatus.CheckedIn.ToString();
                checkedInTicket.LastEventPosition = @event.GlobalPosition;
                break;
            case TicketInvalidated e:
                var invalidatedTicket = await GetOrCreateTicketAsync(e.TicketId, cancellationToken);
                invalidatedTicket.Status = TicketStatus.Invalidated.ToString();
                invalidatedTicket.LastEventPosition = @event.GlobalPosition;
                break;
            }

            if (checkpoint is null)
            {
                checkpoint = new ProjectionCheckpointRecord { Projector = projector, Position = @event.GlobalPosition };
                database.Checkpoints.Add(checkpoint);
            }
            else
                checkpoint.Position = @event.GlobalPosition;
        }
        await database.SaveChangesAsync(cancellationToken);
        // Replays may span hundreds of batches. Detaching here bounds memory and
        // keeps Local lookups proportional to one batch instead of the full history.
        database.ChangeTracker.Clear();
    }

    public async Task ResetAsync(string projector, CancellationToken cancellationToken)
    {
        database.Orders.RemoveRange(database.Orders);
        database.Inventories.RemoveRange(database.Inventories);
        database.Tickets.RemoveRange(database.Tickets);
        var checkpoint = await database.Checkpoints.SingleOrDefaultAsync(x => x.Projector == projector, cancellationToken);
        if (checkpoint is not null) database.Checkpoints.Remove(checkpoint);
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<OrderReadModel> GetOrCreateOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var value = database.Orders.Local.FirstOrDefault(x => x.OrderId == orderId)
            ?? await database.Orders.SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);
        if (value is not null) return value;
        value = new OrderReadModel { OrderId = orderId };
        database.Orders.Add(value);
        return value;
    }

    private async Task<InventoryReadModel> GetOrCreateInventoryAsync(Guid inventoryId, CancellationToken cancellationToken)
    {
        var value = database.Inventories.Local.FirstOrDefault(x => x.InventoryId == inventoryId)
            ?? await database.Inventories.SingleOrDefaultAsync(x => x.InventoryId == inventoryId, cancellationToken);
        if (value is not null) return value;
        value = new InventoryReadModel { InventoryId = inventoryId };
        database.Inventories.Add(value);
        return value;
    }

    private async Task<TicketReadModel> GetOrCreateTicketAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var value = database.Tickets.Local.FirstOrDefault(x => x.TicketId == ticketId)
            ?? await database.Tickets.SingleOrDefaultAsync(x => x.TicketId == ticketId, cancellationToken);
        if (value is not null) return value;
        value = new TicketReadModel { TicketId = ticketId };
        database.Tickets.Add(value);
        return value;
    }
}
