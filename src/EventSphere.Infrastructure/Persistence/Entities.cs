namespace EventSphere.Infrastructure.Persistence;

public sealed class EventRecord
{
    public long GlobalPosition { get; set; }
    public Guid EventId { get; set; }
    public string StreamId { get; set; } = string.Empty;
    public long StreamVersion { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string Metadata { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class OutboxRecord
{
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public Guid CausationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? DeadLetteredAt { get; set; }
}

public sealed class InboxRecord
{
    public Guid MessageId { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class SagaRecord
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid InventoryId { get; set; }
    public Guid ReservationId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "BRL";
    public Guid? TicketId { get; set; }
    public int Step { get; set; }
    public int Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public long Version { get; set; }
}

public sealed class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public string RawKey { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid SagaId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PaymentRecord
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public string Status { get; set; } = "Authorized";
    public string ExternalId { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PricingLockRecord
{
    public Guid SagaId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public bool Active { get; set; }
}

public sealed class InventoryPriceRecord
{
    public Guid InventoryId { get; set; }
    public Guid ShowId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public long Version { get; set; }
}

public sealed class InventoryReservationRecord
{
    public Guid ReservationId { get; set; }
    public Guid InventoryId { get; set; }
    public Guid OrderId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "Active";
}

public sealed class FaultInjectionRecord
{
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset Until { get; set; }
}

public sealed class TicketIndexRecord
{
    public Guid TicketId { get; set; }
    public Guid OrderId { get; set; }
}

public sealed class ProjectionCheckpointRecord
{
    public string Projector { get; set; } = string.Empty;
    public long Position { get; set; }
}

public sealed class SettlementRecord
{
    public Guid OrderId { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal PlatformFee { get; set; }
    public decimal OrganizerAmount { get; set; }
    public string Currency { get; set; } = "BRL";
    public string Status { get; set; } = "Pending";
}

public sealed class NotificationRecord
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Template { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LifecycleWorkflowRecord
{
    public Guid Id { get; set; }
    public int Type { get; set; }
    public Guid AggregateId { get; set; }
    public Guid InitiatorId { get; set; }
    public string Payload { get; set; } = "{}";
    public int Step { get; set; }
    public int Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class SeatLockRecord
{
    public Guid ShowId { get; set; }
    public Guid OwnerId { get; set; }
    public string Sector { get; set; } = string.Empty;
    public string Row { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public Guid ReservationId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
