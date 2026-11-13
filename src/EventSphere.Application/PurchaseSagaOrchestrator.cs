using EventSphere.Domain;

namespace EventSphere.Application;

public sealed class PurchaseSagaOrchestrator(
    ISagaRepository sagas,
    IInventoryParticipant inventory,
    IPricingParticipant pricing,
    IFraudParticipant fraud,
    IPaymentParticipant payments,
    ITicketParticipant tickets,
    ISettlementParticipant settlements,
    INotificationParticipant notifications,
    AggregateRepository aggregates,
    IClock clock)
{
    public async Task ProcessAsync(Guid sagaId, CancellationToken cancellationToken)
    {
        var saga = await sagas.GetAsync(sagaId, cancellationToken)
            ?? throw new InvalidOperationException($"Saga '{sagaId}' was not found.");
        if (saga.Status is SagaStatus.Completed or SagaStatus.Compensated or SagaStatus.ManualIntervention) return;

        var expectedVersion = saga.Version;
        try
        {
            switch (saga.Step)
            {
                case PurchaseStep.Created:
                    await inventory.ReserveAsync(saga, cancellationToken);
                    await AdvanceAsync(saga, PurchaseStep.InventoryReserved, "InventoryReserved", expectedVersion, cancellationToken);
                    break;
                case PurchaseStep.InventoryReserved:
                    await pricing.LockAsync(saga, cancellationToken);
                    await AdvanceAsync(saga, PurchaseStep.PriceLocked, "PriceLocked", expectedVersion, cancellationToken);
                    break;
                case PurchaseStep.PriceLocked:
                    await fraud.ApproveAsync(saga, cancellationToken);
                    await AdvanceAsync(saga, PurchaseStep.FraudApproved, "FraudApproved", expectedVersion, cancellationToken);
                    break;
                case PurchaseStep.FraudApproved:
                    await payments.AuthorizeAsync(saga, cancellationToken);
                    await AdvanceAsync(saga, PurchaseStep.PaymentAuthorized, "PaymentAuthorized", expectedVersion, cancellationToken);
                    break;
                case PurchaseStep.PaymentAuthorized:
                    saga.TicketId = await tickets.IssueAsync(saga, cancellationToken);
                    await AdvanceAsync(saga, PurchaseStep.TicketIssued, "TicketIssued", expectedVersion, cancellationToken);
                    break;
                case PurchaseStep.TicketIssued:
                    await inventory.ConfirmAsync(saga, cancellationToken);
                    await AdvanceAsync(saga, PurchaseStep.InventoryConfirmed, "InventoryConfirmed", expectedVersion, cancellationToken);
                    break;
                case PurchaseStep.InventoryConfirmed:
                    await settlements.RecordSaleAsync(saga, cancellationToken);
                    await AdvanceAsync(saga, PurchaseStep.SettlementRecorded, "SettlementRecorded", expectedVersion, cancellationToken);
                    break;
                case PurchaseStep.SettlementRecorded:
                    await notifications.NotifyOrderCompletedAsync(saga, cancellationToken);
                    await AdvanceAsync(saga, PurchaseStep.CustomerNotified, "CustomerNotified", expectedVersion, cancellationToken);
                    break;
                case PurchaseStep.CustomerNotified:
                    await CompleteAsync(saga, expectedVersion, cancellationToken);
                    break;
            }
        }
        catch (ExpectedVersionException exception)
        {
            saga.Attempts++;
            saga.LastError = exception.Message;
            saga.NextAttemptAt = clock.UtcNow.AddMilliseconds(25 + Random.Shared.Next(75));
            saga.LeaseUntil = null;
            saga.UpdatedAt = clock.UtcNow;
            try { await sagas.SaveAsync(saga, expectedVersion, cancellationToken); }
            catch (ExpectedVersionException) { }
        }
        catch (DomainRuleViolationException exception)
        {
            saga.Attempts++;
            saga.LastError = exception.Message;
            saga.Status = SagaStatus.Compensating;
            saga.LeaseUntil = null;
            saga.UpdatedAt = clock.UtcNow;
            await sagas.SaveAsync(saga, expectedVersion, cancellationToken);
            await CompensateAsync(saga.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            saga.Attempts++;
            saga.LastError = exception.Message;
            saga.UpdatedAt = clock.UtcNow;
            if (saga.Attempts < 10)
            {
                saga.NextAttemptAt = clock.UtcNow.AddSeconds(Math.Pow(2, saga.Attempts));
                saga.LeaseUntil = null;
                await sagas.SaveAsync(saga, expectedVersion, cancellationToken);
                return;
            }

            saga.Status = SagaStatus.Compensating;
            await sagas.SaveAsync(saga, expectedVersion, cancellationToken);
            await CompensateAsync(saga.Id, cancellationToken);
        }
    }

    public async Task CompensateAsync(Guid sagaId, CancellationToken cancellationToken)
    {
        var saga = await sagas.GetAsync(sagaId, cancellationToken)
            ?? throw new InvalidOperationException($"Saga '{sagaId}' was not found.");
        if (saga.Status == SagaStatus.Compensated) return;
        var expectedVersion = saga.Version;
        try
        {
            if (saga.TicketId.HasValue) await tickets.InvalidateAsync(saga.TicketId.Value, "Purchase saga compensation", cancellationToken);
            if (saga.Step >= PurchaseStep.SettlementRecorded) await settlements.ReverseSaleAsync(saga, cancellationToken);
            if (saga.Step >= PurchaseStep.PaymentAuthorized) await payments.CancelAsync(saga, cancellationToken);
            if (saga.Step >= PurchaseStep.PriceLocked) await pricing.RestoreAsync(saga, cancellationToken);
            if (saga.Step >= PurchaseStep.InventoryConfirmed)
                await inventory.ReturnAsync(saga, "Purchase saga compensation", cancellationToken);
            else if (saga.Step >= PurchaseStep.InventoryReserved)
                await inventory.ReleaseAsync(saga, "Purchase saga compensation", cancellationToken);

            var order = await aggregates.LoadAsync<OrderAggregate>(Streams.Order(saga.OrderId), cancellationToken);
            if (order.Status is not OrderStatus.Cancelled and not OrderStatus.Completed)
            {
                order.Cancel(saga.LastError ?? "Saga failed", clock.UtcNow);
                await aggregates.SaveAsync(Streams.Order(saga.OrderId), order, Metadata(saga), cancellationToken);
            }

            saga.Status = SagaStatus.Compensated;
            EventSphereTelemetry.SagaCompensations.Add(1);
            saga.LeaseUntil = null;
            saga.UpdatedAt = clock.UtcNow;
            await sagas.SaveAsync(saga, expectedVersion, cancellationToken);
        }
        catch (ExpectedVersionException)
        {
            // Another leased worker completed the same idempotent compensation.
            // Its durable state is authoritative; a later claim resumes if needed.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            saga.Attempts++;
            saga.Status = saga.Attempts >= 20 ? SagaStatus.ManualIntervention : SagaStatus.Compensating;
            saga.LastError = $"Compensation attempt {saga.Attempts} failed: {exception.Message}";
            saga.LeaseUntil = null;
            saga.NextAttemptAt = clock.UtcNow.AddMilliseconds(Math.Min(5_000, 100 * saga.Attempts));
            saga.UpdatedAt = clock.UtcNow;
            try { await sagas.SaveAsync(saga, expectedVersion, cancellationToken); }
            catch (ExpectedVersionException) { }
        }
    }

    private async Task AdvanceAsync(PurchaseSagaState saga, PurchaseStep step, string orderStep,
        long expectedVersion, CancellationToken cancellationToken)
    {
        var order = await aggregates.LoadAsync<OrderAggregate>(Streams.Order(saga.OrderId), cancellationToken);
        order.CompleteStep(orderStep, clock.UtcNow);
        await aggregates.SaveAsync(Streams.Order(saga.OrderId), order, Metadata(saga), cancellationToken);
        saga.Step = step;
        EventSphereTelemetry.SagaTransitions.Add(1, new KeyValuePair<string, object?>("step", step.ToString()));
        saga.Attempts = 0;
        saga.LastError = null;
        saga.UpdatedAt = clock.UtcNow;
        saga.NextAttemptAt = clock.UtcNow;
        await sagas.SaveAsync(saga, expectedVersion, cancellationToken);
    }

    private async Task CompleteAsync(PurchaseSagaState saga, long expectedVersion, CancellationToken cancellationToken)
    {
        var order = await aggregates.LoadAsync<OrderAggregate>(Streams.Order(saga.OrderId), cancellationToken);
        order.Complete(saga.TicketId ?? throw new InvalidOperationException("Saga has no ticket."), clock.UtcNow);
        await aggregates.SaveAsync(Streams.Order(saga.OrderId), order, Metadata(saga), cancellationToken);
        saga.Step = PurchaseStep.Completed;
        saga.Status = SagaStatus.Completed;
        saga.UpdatedAt = clock.UtcNow;
        saga.LeaseUntil = null;
        await sagas.SaveAsync(saga, expectedVersion, cancellationToken);
    }

    private static EventMetadata Metadata(PurchaseSagaState saga) =>
        new(saga.Id, saga.Id, $"saga:{saga.Id:N}", string.Empty);
}
