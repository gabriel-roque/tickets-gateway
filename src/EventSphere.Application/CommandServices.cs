using EventSphere.Domain;

namespace EventSphere.Application;

public sealed class InventoryCommandService(IEventStore eventStore, IClock clock)
{
    public async Task<Guid> CreateAsync(CreateInventoryCommand command, EventMetadata metadata,
        CancellationToken cancellationToken)
    {
        var showHistory = await eventStore.ReadStreamAsync(Streams.Show(command.ShowId), cancellationToken);
        var show = ShowAggregate.For(command.ShowId);
        show.LoadFromHistory(showHistory.Select(x => x.Data));
        show.RegisterInventory(command.InventoryId, clock.UtcNow);
        var aggregate = InventoryAggregate.Create(command.InventoryId, command.ShowId, command.SectorId,
            command.BucketId, command.Capacity, Money.Create(command.UnitPrice, command.Currency), clock.UtcNow);
        await eventStore.AppendManyAsync([
            new EventStreamAppend(Streams.Show(command.ShowId), show.ExpectedVersion,
                show.UncommittedEvents, metadata),
            new EventStreamAppend(Streams.Inventory(command.InventoryId), -1,
                aggregate.UncommittedEvents, metadata)
        ], cancellationToken);
        show.MarkChangesAsCommitted();
        aggregate.MarkChangesAsCommitted();
        return command.InventoryId;
    }
}

public sealed class OrderCommandService(
    IIdempotencyStore idempotencyStore,
    IOrderSubmissionStore submissionStore,
    AggregateRepository aggregates,
    IClock clock)
{
    public async Task<OperationAccepted> CreateAsync(CreateOrderCommand command, EventMetadata metadata,
        CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var rawKey = IdempotencyKey.Create(command.IdempotencyKey).Value;
        var scopedKey = OrderIdempotency.ScopedKey(command);
        var fingerprint = OrderIdempotency.Fingerprint(command);
        var key = IdempotencyKey.Create(scopedKey);
        var existingOrder = await idempotencyStore.FindOrderAsync(scopedKey, cancellationToken);
        existingOrder ??= await idempotencyStore.FindLegacyOrderAsync(rawKey, command.CustomerId, cancellationToken);
        if (existingOrder is not null)
        {
            if (!string.Equals(existingOrder.Fingerprint, fingerprint, StringComparison.Ordinal))
                throw new IdempotencyConflictException("The idempotency key was already used with a different request.");
            return new OperationAccepted(existingOrder.SagaId, existingOrder.OrderId,
                $"/api/v2/operations/{existingOrder.SagaId}");
        }

        var now = clock.UtcNow;
        var orderId = command.OrderId ?? Guid.NewGuid();
        var reservationId = command.ReservationId ?? Guid.NewGuid();
        var sagaId = command.SagaId ?? Guid.NewGuid();
        var inventory = await aggregates.LoadAsync<InventoryAggregate>(
            Streams.Inventory(command.InventoryId), cancellationToken);
        if (!inventory.SalesOpen) throw new DomainRuleViolationException("Sales are closed for this inventory.");
        var price = inventory.UnitPrice;
        var order = OrderAggregate.Create(orderId, command.CustomerId, command.InventoryId,
            reservationId, command.Quantity, price, key, now);
        var saga = new PurchaseSagaState
        {
            Id = sagaId,
            OrderId = orderId,
            CustomerId = command.CustomerId,
            InventoryId = command.InventoryId,
            ReservationId = reservationId,
            Quantity = command.Quantity,
            UnitPrice = price,
            Step = PurchaseStep.Created,
            Status = SagaStatus.Running,
            CreatedAt = now,
            UpdatedAt = now,
            NextAttemptAt = now
        };

        var submitted = await submissionStore.SubmitAsync(
            order, saga, rawKey, fingerprint, metadata, cancellationToken);
        EventSphereTelemetry.OrdersAccepted.Add(1);
        EventSphereTelemetry.CommandDuration.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return new OperationAccepted(submitted.SagaId, submitted.OrderId,
            $"/api/v2/operations/{submitted.SagaId}");
    }
}

public sealed class DirectOrderIngress(OrderCommandService commands) : IOrderIngress
{
    public Task<OperationAccepted> SubmitAsync(CreateOrderCommand command, EventMetadata metadata,
        CancellationToken cancellationToken) => commands.CreateAsync(command, metadata, cancellationToken);
}

public sealed class TicketCommandService(AggregateRepository repository, IClock clock)
{
    public async Task TransferAsync(Guid ticketId, Guid newOwnerId, EventMetadata metadata,
        CancellationToken cancellationToken)
    {
        var ticket = await repository.LoadAsync<TicketAggregate>(Streams.Ticket(ticketId), cancellationToken);
        ticket.Transfer(newOwnerId, $"qr-{Guid.NewGuid():N}", clock.UtcNow);
        await repository.SaveAsync(Streams.Ticket(ticketId), ticket, metadata, cancellationToken);
    }

    public async Task CheckInAsync(Guid ticketId, string gateId, EventMetadata metadata,
        CancellationToken cancellationToken)
    {
        var ticket = await repository.LoadAsync<TicketAggregate>(Streams.Ticket(ticketId), cancellationToken);
        ticket.CheckIn(gateId, clock.UtcNow);
        await repository.SaveAsync(Streams.Ticket(ticketId), ticket, metadata, cancellationToken);
    }
}
