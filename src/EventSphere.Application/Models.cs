using System.Security.Cryptography;
using System.Text;
using EventSphere.Domain;

namespace EventSphere.Application;

public enum SagaStatus { Running, Completed, Compensating, Compensated, Failed, ManualIntervention }
public enum PurchaseStep
{
    Created, InventoryReserved, PriceLocked, FraudApproved, PaymentAuthorized, TicketIssued,
    InventoryConfirmed, SettlementRecorded, CustomerNotified, Completed
}

public enum LifecycleWorkflowType { Refund, EventCancellation, TicketTransfer }
public enum LifecycleWorkflowStatus { Running, Completed, Failed, ManualIntervention }

public sealed class LifecycleWorkflowState
{
    public Guid Id { get; init; }
    public LifecycleWorkflowType Type { get; init; }
    public Guid AggregateId { get; init; }
    public Guid InitiatorId { get; init; }
    public string Payload { get; set; } = "{}";
    public int Step { get; set; }
    public LifecycleWorkflowStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class PurchaseSagaState
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid InventoryId { get; init; }
    public Guid ReservationId { get; init; }
    public int Quantity { get; init; }
    public Money UnitPrice { get; init; }
    public Guid? TicketId { get; set; }
    public PurchaseStep Step { get; set; }
    public SagaStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public long Version { get; set; }
}

public sealed record CreateInventoryCommand(
    Guid InventoryId, Guid ShowId, string SectorId, string BucketId, int Capacity,
    decimal UnitPrice, string Currency = "BRL");

public sealed record CreateOrderCommand(
    Guid CustomerId, Guid InventoryId, int Quantity, string IdempotencyKey,
    Guid? OrderId = null, Guid? ReservationId = null, Guid? SagaId = null);

public static class OrderIdempotency
{
    public static string ScopedKey(CreateOrderCommand command)
    {
        var raw = IdempotencyKey.Create(command.IdempotencyKey).Value;
        return Hash($"key|{command.CustomerId:N}|{raw}");
    }

    public static string Fingerprint(CreateOrderCommand command) =>
        Hash($"request|{command.CustomerId:N}|{command.InventoryId:N}|{command.Quantity}");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed record LockSeatCommand(
    Guid OwnerId, Guid ShowId, string Sector, string Row, string Number, Guid ReservationId);

public sealed record OperationAccepted(Guid OperationId, Guid OrderId, string StatusUrl);

public sealed class OrderReadModel
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid InventoryId { get; set; }
    public Guid ReservationId { get; set; }
    public int Quantity { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "BRL";
    public string Status { get; set; } = "Pending";
    public Guid? TicketId { get; set; }
    public long LastEventPosition { get; set; }
}

public sealed class InventoryReadModel
{
    public Guid InventoryId { get; set; }
    public Guid ShowId { get; set; }
    public string SectorId { get; set; } = string.Empty;
    public string BucketId { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int Reserved { get; set; }
    public int Confirmed { get; set; }
    public int Available { get; set; }
    public bool SalesOpen { get; set; }
    public long LastEventPosition { get; set; }
}

public sealed class TicketReadModel
{
    public Guid TicketId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OwnerId { get; set; }
    public string QrCode { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public long LastEventPosition { get; set; }
}
