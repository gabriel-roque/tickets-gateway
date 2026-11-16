using System.Text.Json;
using EventSphere.Domain;

namespace EventSphere.Application;

public sealed class LifecycleSagaService(
    ILifecycleWorkflowRepository workflows,
    AggregateRepository aggregates,
    IEventStore eventStore,
    ISagaRepository sagas,
    IInventoryParticipant inventory,
    IPaymentParticipant payments,
    ITicketParticipant tickets,
    ISettlementParticipant settlements,
    INotificationParticipant notifications,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> StartRefundAsync(Guid orderId, string reason, CancellationToken cancellationToken)
    {
        var id = DeterministicId($"refund:{orderId:N}");
        if (await workflows.GetAsync(id, cancellationToken) is not null) return id;
        var order = await aggregates.LoadAsync<OrderAggregate>(Streams.Order(orderId), cancellationToken);
        var purchaseSaga = await sagas.GetByOrderAsync(orderId, cancellationToken);
        if (order.Status != OrderStatus.Completed || purchaseSaga?.Status != SagaStatus.Completed)
            throw new DomainRuleViolationException("Refunds require a completed purchase.");
        await workflows.CreateAsync(new LifecycleWorkflowState
        {
            Id = id,
            Type = LifecycleWorkflowType.Refund,
            AggregateId = orderId,
            InitiatorId = order.CustomerId,
            Payload = JsonSerializer.Serialize(new RefundPayload(reason), JsonOptions),
            Status = LifecycleWorkflowStatus.Running,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow
        }, cancellationToken);
        return id;
    }

    public async Task<Guid> StartEventCancellationAsync(Guid showId, CancellationToken cancellationToken)
    {
        var history = await eventStore.ReadStreamAsync(Streams.Show(showId), cancellationToken);
        if (history.Count == 0) throw new DomainRuleViolationException("Show inventory was not found.");
        var show = ShowAggregate.For(showId);
        show.LoadFromHistory(history.Select(x => x.Data));
        show.Cancel("Show cancelled", clock.UtcNow);
        if (show.UncommittedEvents.Count > 0)
        {
            await eventStore.AppendAsync(Streams.Show(showId), show.ExpectedVersion,
                show.UncommittedEvents, new EventMetadata(showId, showId, "event-cancellation", string.Empty),
                cancellationToken);
            show.MarkChangesAsCommitted();
        }
        foreach (var inventoryId in show.InventoryIds)
            await CloseInventoryAsync(inventoryId, cancellationToken);
        var id = DeterministicId($"cancel-event:{showId:N}");
        await workflows.CreateAsync(new LifecycleWorkflowState
        {
            Id = id,
            Type = LifecycleWorkflowType.EventCancellation,
            AggregateId = showId,
            Payload = JsonSerializer.Serialize(
                new EventCancellationPayload(show.InventoryIds.ToArray(), [], 0, false), JsonOptions),
            Status = LifecycleWorkflowStatus.Running,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow
        }, cancellationToken);
        return id;
    }

    public async Task<Guid> StartTransferAsync(Guid ticketId, Guid newOwnerId, Guid actorId, bool isAdmin,
        CancellationToken cancellationToken)
    {
        if (newOwnerId == Guid.Empty) throw new DomainRuleViolationException("New owner is required.");
        var ticket = await aggregates.LoadAsync<TicketAggregate>(Streams.Ticket(ticketId), cancellationToken);
        if (!isAdmin && ticket.OwnerId != actorId)
            throw new DomainRuleViolationException("Only the current ticket owner can transfer it.");
        if (ticket.Status != TicketStatus.Active || ticket.OwnerId == newOwnerId)
            throw new DomainRuleViolationException("An active ticket and a different new owner are required.");
        var id = DeterministicId($"transfer:{ticketId:N}:{ticket.OwnerId:N}:{newOwnerId:N}");
        await workflows.CreateAsync(new LifecycleWorkflowState
        {
            Id = id,
            Type = LifecycleWorkflowType.TicketTransfer,
            AggregateId = ticketId,
            InitiatorId = actorId,
            Payload = JsonSerializer.Serialize(new TransferPayload(ticket.OwnerId, newOwnerId), JsonOptions),
            Status = LifecycleWorkflowStatus.Running,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow
        }, cancellationToken);
        return id;
    }

    public async Task ProcessAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var workflow = await workflows.GetAsync(workflowId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow '{workflowId}' was not found.");
        if (workflow.Status != LifecycleWorkflowStatus.Running) return;
        var expectedVersion = workflow.Version;
        try
        {
            switch (workflow.Type)
            {
                case LifecycleWorkflowType.Refund:
                    await ProcessRefundAsync(workflow, cancellationToken);
                    break;
                case LifecycleWorkflowType.EventCancellation:
                    await ProcessEventCancellationAsync(workflow, cancellationToken);
                    break;
                case LifecycleWorkflowType.TicketTransfer:
                    await ProcessTransferAsync(workflow, cancellationToken);
                    break;
            }
            workflow.Attempts = 0;
            if (workflow.Status != LifecycleWorkflowStatus.Failed) workflow.LastError = null;
            workflow.LeaseUntil = null;
            workflow.UpdatedAt = clock.UtcNow;
            workflow.NextAttemptAt = clock.UtcNow;
            await workflows.SaveAsync(workflow, expectedVersion, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ExpectedVersionException)
        {
            workflow.Attempts++;
            workflow.LastError = exception.Message;
            workflow.LeaseUntil = null;
            workflow.UpdatedAt = clock.UtcNow;
            workflow.NextAttemptAt = clock.UtcNow.AddSeconds(Math.Pow(2, Math.Min(workflow.Attempts, 6)));
            if (workflow.Attempts >= 10) workflow.Status = LifecycleWorkflowStatus.ManualIntervention;
            await workflows.SaveAsync(workflow, expectedVersion, cancellationToken);
        }
    }

    private async Task ProcessRefundAsync(LifecycleWorkflowState workflow, CancellationToken cancellationToken)
    {
        var order = await aggregates.LoadAsync<OrderAggregate>(Streams.Order(workflow.AggregateId), cancellationToken);
        var payload = JsonSerializer.Deserialize<RefundPayload>(workflow.Payload, JsonOptions)
            ?? throw new InvalidOperationException("Refund payload is invalid.");
        var purchaseSaga = await sagas.GetByOrderAsync(order.Id, cancellationToken)
            ?? throw new InvalidOperationException("Purchase saga was not found for refund.");
        switch (workflow.Step)
        {
            case 0:
                var history = await eventStore.ReadStreamAsync(Streams.Order(workflow.AggregateId), cancellationToken);
                var alreadyRequested = history.Any(stored => stored.Metadata.CorrelationId == workflow.Id
                    && stored.Data is RefundRequested);
                if (!alreadyRequested && order.Status == OrderStatus.Completed)
                {
                    order.RequestRefund(payload.Reason, clock.UtcNow);
                    await aggregates.SaveAsync(Streams.Order(order.Id), order, Metadata(workflow), cancellationToken);
                }
                else if (!alreadyRequested)
                {
                    workflow.Status = LifecycleWorkflowStatus.Failed;
                    workflow.LastError = "Order was no longer eligible when the refund started.";
                    return;
                }
                workflow.Step = 1;
                break;
            case 1:
                if (order.TicketId.HasValue) await tickets.InvalidateAsync(order.TicketId.Value, "Refund", cancellationToken);
                workflow.Step = 2;
                break;
            case 2:
                await payments.RefundAsync(order.Id, order.Total, cancellationToken);
                workflow.Step = 3;
                break;
            case 3:
                await settlements.ReverseSaleAsync(purchaseSaga, cancellationToken);
                workflow.Step = 4;
                break;
            case 4:
                await inventory.ReturnAsync(purchaseSaga, "Refund", cancellationToken);
                workflow.Step = 5;
                break;
            case 5:
                order = await aggregates.LoadAsync<OrderAggregate>(Streams.Order(workflow.AggregateId), cancellationToken);
                order.MarkRefunded(clock.UtcNow);
                await aggregates.SaveAsync(Streams.Order(order.Id), order, Metadata(workflow), cancellationToken);
                workflow.Step = 6;
                break;
            case 6:
                await notifications.NotifyAsync(order.Id, "refund-completed", cancellationToken);
                workflow.Step = 7;
                workflow.Status = LifecycleWorkflowStatus.Completed;
                break;
        }
    }

    private async Task ProcessTransferAsync(LifecycleWorkflowState workflow, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<TransferPayload>(workflow.Payload, JsonOptions)
            ?? throw new InvalidOperationException("Transfer payload is invalid.");
        var ticket = await aggregates.LoadAsync<TicketAggregate>(Streams.Ticket(workflow.AggregateId), cancellationToken);
        switch (workflow.Step)
        {
            case 0:
                var history = await eventStore.ReadStreamAsync(Streams.Ticket(workflow.AggregateId), cancellationToken);
                var alreadyTransferred = history.Any(stored => stored.Metadata.CorrelationId == workflow.Id
                    && stored.Data is TicketTransferred transferred
                    && transferred.NewOwnerId == payload.NewOwnerId);
                if (!alreadyTransferred && ticket.OwnerId == payload.ExpectedOwnerId)
                {
                    ticket.Transfer(payload.NewOwnerId, $"qr-{Guid.NewGuid():N}", clock.UtcNow);
                    await aggregates.SaveAsync(Streams.Ticket(ticket.Id), ticket, Metadata(workflow), cancellationToken);
                }
                else if (!alreadyTransferred)
                {
                    workflow.Status = LifecycleWorkflowStatus.Failed;
                    workflow.LastError = "Ticket ownership changed before this transfer could execute.";
                    return;
                }
                workflow.Step = 1;
                break;
            case 1:
                await notifications.NotifyAsync(ticket.OrderId,
                    $"ticket-transferred:{workflow.Id:N}", cancellationToken);
                workflow.Step = 2;
                workflow.Status = LifecycleWorkflowStatus.Completed;
                break;
        }
    }

    private async Task<CancellationScope> DiscoverCancellationScopeAsync(
        IReadOnlySet<Guid> inventoryIds, CancellationToken cancellationToken)
    {
        var orderInventory = new Dictionary<Guid, Guid>();
        var completed = new HashSet<Guid>();
        var refunded = new HashSet<Guid>();
        long position = 0;
        while (true)
        {
            var batch = await eventStore.ReadAllAsync(position, 1000, cancellationToken);
            if (batch.Count == 0) break;
            foreach (var stored in batch)
            {
                switch (stored.Data)
                {
                    case OrderCreated e:
                        orderInventory[e.OrderId] = e.InventoryId;
                        break;
                    case OrderCompleted e:
                        completed.Add(e.OrderId);
                        break;
                    case OrderRefunded e:
                        refunded.Add(e.OrderId);
                        break;
                }
            }
            position = batch[^1].GlobalPosition;
        }
        var relevantOrders = orderInventory.Where(x => inventoryIds.Contains(x.Value)).Select(x => x.Key).ToArray();
        var hasActiveSagas = false;
        foreach (var orderId in relevantOrders)
        {
            var saga = await sagas.GetByOrderAsync(orderId, cancellationToken);
            if (saga?.Status is SagaStatus.Running or SagaStatus.Compensating)
            {
                hasActiveSagas = true;
                break;
            }
        }
        var orders = completed.Where(x => !refunded.Contains(x)
            && orderInventory.TryGetValue(x, out var inventoryId) && inventoryIds.Contains(inventoryId)).ToArray();
        return new CancellationScope(orders, hasActiveSagas);
    }

    private async Task CloseInventoryAsync(Guid inventoryId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var aggregate = await aggregates.LoadAsync<InventoryAggregate>(Streams.Inventory(inventoryId), cancellationToken);
            aggregate.CloseSales("Show cancelled", clock.UtcNow);
            try
            {
                await aggregates.SaveAsync(Streams.Inventory(inventoryId), aggregate,
                    new EventMetadata(inventoryId, inventoryId, "event-cancellation", string.Empty), cancellationToken);
                return;
            }
            catch (ExpectedVersionException) when (attempt < 9) { }
        }
    }

    private async Task ProcessEventCancellationAsync(LifecycleWorkflowState workflow, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<EventCancellationPayload>(workflow.Payload, JsonOptions)
            ?? throw new InvalidOperationException("Event cancellation payload is invalid.");
        if (!payload.ScopeReady)
        {
            var scope = await DiscoverCancellationScopeAsync(payload.InventoryIds.ToHashSet(), cancellationToken);
            if (scope.HasActiveSagas)
            {
                workflow.NextAttemptAt = clock.UtcNow.AddSeconds(1);
                return;
            }
            payload = payload with { OrderIds = scope.CompletedOrderIds, ScopeReady = true };
            workflow.Payload = JsonSerializer.Serialize(payload, JsonOptions);
        }
        if (payload.Index >= payload.OrderIds.Length)
        {
            workflow.Status = LifecycleWorkflowStatus.Completed;
            return;
        }

        var childId = await StartRefundAsync(payload.OrderIds[payload.Index], "Event cancelled", cancellationToken);
        var child = await workflows.GetAsync(childId, cancellationToken);
        if (child?.Status == LifecycleWorkflowStatus.Completed)
        {
            workflow.Payload = JsonSerializer.Serialize(payload with { Index = payload.Index + 1 }, JsonOptions);
            workflow.Step = payload.Index + 1;
        }
        else
        {
            workflow.NextAttemptAt = clock.UtcNow.AddSeconds(1);
        }
    }

    private static EventMetadata Metadata(LifecycleWorkflowState workflow) =>
        new(workflow.Id, workflow.Id, $"workflow:{workflow.Id:N}", string.Empty);

    private static Guid DeterministicId(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record RefundPayload(string Reason);
    private sealed record EventCancellationPayload(
        Guid[] InventoryIds, Guid[] OrderIds, int Index, bool ScopeReady);
    private sealed record TransferPayload(Guid ExpectedOwnerId, Guid NewOwnerId);
    private sealed record CancellationScope(Guid[] CompletedOrderIds, bool HasActiveSagas);
}
