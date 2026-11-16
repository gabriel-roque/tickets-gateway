using EventSphere.Domain;

namespace EventSphere.Application;

public sealed record EventMetadata(
    Guid CorrelationId, Guid CausationId, string ActorId, string TraceId);

public sealed record StoredEvent(
    long GlobalPosition, string StreamId, long StreamVersion, IDomainEvent Data, EventMetadata Metadata);

public sealed record EventStreamAppend(
    string StreamId, long ExpectedVersion, IReadOnlyCollection<IDomainEvent> Events, EventMetadata Metadata);

public interface IEventStore
{
    Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(string streamId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredEvent>> ReadAllAsync(long afterPosition, int batchSize, CancellationToken cancellationToken);
    Task AppendAsync(string streamId, long expectedVersion, IReadOnlyCollection<IDomainEvent> events,
        EventMetadata metadata, CancellationToken cancellationToken);
    Task AppendManyAsync(IReadOnlyCollection<EventStreamAppend> appends, CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IMessagePublisher
{
    Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken);
}

public sealed record IntegrationMessage(
    Guid MessageId, string MessageType, string Payload, string PartitionKey,
    Guid CorrelationId, Guid CausationId, DateTimeOffset OccurredAt);

public interface IInbox
{
    Task<bool> ExecuteOnceAsync(IntegrationMessage message, Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}

public interface IIdempotencyStore
{
    Task<OrderSubmissionResult?> FindOrderAsync(string key, CancellationToken cancellationToken);
    Task<OrderSubmissionResult?> FindLegacyOrderAsync(string rawKey, Guid customerId,
        CancellationToken cancellationToken);
}

public interface IOrderIngress
{
    Task<OperationAccepted> SubmitAsync(CreateOrderCommand command, EventMetadata metadata,
        CancellationToken cancellationToken);
}

public sealed record OrderSubmissionResult(Guid OrderId, Guid SagaId, string Fingerprint);

public interface IOrderSubmissionStore
{
    Task<OrderSubmissionResult> SubmitAsync(OrderAggregate order, PurchaseSagaState saga,
        string rawIdempotencyKey, string fingerprint, EventMetadata metadata, CancellationToken cancellationToken);
}

public interface ISagaRepository
{
    Task<PurchaseSagaState?> GetAsync(Guid sagaId, CancellationToken cancellationToken);
    Task<PurchaseSagaState?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseSagaState>> ClaimRunnableAsync(int batchSize, TimeSpan lease,
        CancellationToken cancellationToken);
    Task SaveAsync(PurchaseSagaState saga, long expectedVersion, CancellationToken cancellationToken);
}

public interface IReadModelStore
{
    Task<OrderReadModel?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<InventoryReadModel?> GetInventoryAsync(Guid inventoryId, CancellationToken cancellationToken);
    Task<TicketReadModel?> GetTicketAsync(Guid ticketId, CancellationToken cancellationToken);
    Task<long> GetCheckpointAsync(string projector, CancellationToken cancellationToken);
    Task ProjectAsync(string projector, StoredEvent @event, CancellationToken cancellationToken);
    Task ProjectBatchAsync(string projector, IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken);
    Task ResetAsync(string projector, CancellationToken cancellationToken);
}

public interface IInventoryParticipant
{
    Task ReserveAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
    Task ConfirmAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
    Task ReleaseAsync(PurchaseSagaState saga, string reason, CancellationToken cancellationToken);
    Task ReturnAsync(PurchaseSagaState saga, string reason, CancellationToken cancellationToken);
    Task<int> ExpireDueAsync(int batchSize, CancellationToken cancellationToken);
}

public interface IPriceCatalog
{
    Task SetAsync(Guid inventoryId, Guid showId, Money price, CancellationToken cancellationToken);
    Task<Money> GetAsync(Guid inventoryId, CancellationToken cancellationToken);
}

public interface IPricingParticipant
{
    Task LockAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
    Task RestoreAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
}

public interface IFraudParticipant
{
    Task ApproveAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
}

public interface IPaymentParticipant
{
    Task AuthorizeAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
    Task CancelAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
    Task RefundAsync(Guid orderId, Money amount, CancellationToken cancellationToken);
}

public interface ITicketParticipant
{
    Task<Guid> IssueAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid ticketId, string reason, CancellationToken cancellationToken);
}

public interface ISettlementParticipant
{
    Task RecordSaleAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
    Task ReverseSaleAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
}

public interface INotificationParticipant
{
    Task NotifyOrderCompletedAsync(PurchaseSagaState saga, CancellationToken cancellationToken);
    Task NotifyAsync(Guid orderId, string template, CancellationToken cancellationToken);
}

public interface ISeatLockService
{
    Task LockAsync(LockSeatCommand command, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid reservationId, Guid actorId, bool isAdmin, CancellationToken cancellationToken);
}

public interface ILifecycleWorkflowRepository
{
    Task<LifecycleWorkflowState?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task CreateAsync(LifecycleWorkflowState workflow, CancellationToken cancellationToken);
    Task SaveAsync(LifecycleWorkflowState workflow, long expectedVersion, CancellationToken cancellationToken);
    Task<IReadOnlyList<LifecycleWorkflowState>> ClaimRunnableAsync(int batchSize, TimeSpan lease,
        CancellationToken cancellationToken);
}

public static class Streams
{
    public static string Show(Guid id) => $"show-{id:N}";
    public static string Inventory(Guid id) => $"inventory-{id:N}";
    public static string Order(Guid id) => $"order-{id:N}";
    public static string Ticket(Guid id) => $"ticket-{id:N}";
}
