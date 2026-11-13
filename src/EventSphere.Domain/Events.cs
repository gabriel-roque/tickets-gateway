namespace EventSphere.Domain;

public sealed record InventoryCreated(
    Guid EventId, DateTimeOffset OccurredAt, Guid InventoryId, Guid ShowId, string SectorId,
    string BucketId, int Capacity, Money UnitPrice) : DomainEvent(EventId, OccurredAt);

public sealed record ShowInventoryRegistered(
    Guid EventId, DateTimeOffset OccurredAt, Guid ShowId, Guid InventoryId)
    : DomainEvent(EventId, OccurredAt);

public sealed record ShowCancelled(
    Guid EventId, DateTimeOffset OccurredAt, Guid ShowId, string Reason)
    : DomainEvent(EventId, OccurredAt);

public sealed record InventoryReserved(
    Guid EventId, DateTimeOffset OccurredAt, Guid InventoryId, Guid ReservationId,
    Guid OrderId, int Quantity, DateTimeOffset ExpiresAt) : DomainEvent(EventId, OccurredAt);

public sealed record InventoryReleased(
    Guid EventId, DateTimeOffset OccurredAt, Guid InventoryId, Guid ReservationId,
    int Quantity, string Reason) : DomainEvent(EventId, OccurredAt);

public sealed record InventoryConfirmed(
    Guid EventId, DateTimeOffset OccurredAt, Guid InventoryId, Guid ReservationId, int Quantity)
    : DomainEvent(EventId, OccurredAt);

public sealed record InventoryReturned(
    Guid EventId, DateTimeOffset OccurredAt, Guid InventoryId, Guid ReservationId, int Quantity, string Reason)
    : DomainEvent(EventId, OccurredAt);

public sealed record InventoryReservationExpired(
    Guid EventId, DateTimeOffset OccurredAt, Guid InventoryId, Guid ReservationId, int Quantity)
    : DomainEvent(EventId, OccurredAt);

public sealed record InventorySalesClosed(
    Guid EventId, DateTimeOffset OccurredAt, Guid InventoryId, string Reason)
    : DomainEvent(EventId, OccurredAt);

public sealed record OrderCreated(
    Guid EventId, DateTimeOffset OccurredAt, Guid OrderId, Guid CustomerId, Guid InventoryId,
    Guid ReservationId, int Quantity, Money UnitPrice, string IdempotencyKey)
    : DomainEvent(EventId, OccurredAt);

public sealed record OrderStepCompleted(
    Guid EventId, DateTimeOffset OccurredAt, Guid OrderId, string Step)
    : DomainEvent(EventId, OccurredAt);

public sealed record OrderCompleted(
    Guid EventId, DateTimeOffset OccurredAt, Guid OrderId, Guid TicketId)
    : DomainEvent(EventId, OccurredAt);

public sealed record OrderCancelled(
    Guid EventId, DateTimeOffset OccurredAt, Guid OrderId, string Reason)
    : DomainEvent(EventId, OccurredAt);

public sealed record RefundRequested(
    Guid EventId, DateTimeOffset OccurredAt, Guid OrderId, string Reason)
    : DomainEvent(EventId, OccurredAt);

public sealed record OrderRefunded(
    Guid EventId, DateTimeOffset OccurredAt, Guid OrderId, Money Amount)
    : DomainEvent(EventId, OccurredAt);

public sealed record TicketIssued(
    Guid EventId, DateTimeOffset OccurredAt, Guid TicketId, Guid OrderId, Guid OwnerId,
    string QrCode) : DomainEvent(EventId, OccurredAt);

public sealed record TicketTransferred(
    Guid EventId, DateTimeOffset OccurredAt, Guid TicketId, Guid PreviousOwnerId,
    Guid NewOwnerId, string NewQrCode) : DomainEvent(EventId, OccurredAt);

public sealed record TicketCheckedIn(
    Guid EventId, DateTimeOffset OccurredAt, Guid TicketId, string GateId)
    : DomainEvent(EventId, OccurredAt);

public sealed record TicketInvalidated(
    Guid EventId, DateTimeOffset OccurredAt, Guid TicketId, string Reason)
    : DomainEvent(EventId, OccurredAt);
