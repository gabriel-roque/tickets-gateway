namespace EventSphere.Domain;

public enum OrderStatus { Pending, Processing, Completed, Cancelled, Refunding, Refunded }

public sealed class OrderAggregate : AggregateRoot
{
    private readonly HashSet<string> _completedSteps = new(StringComparer.Ordinal);

    public Guid CustomerId { get; private set; }
    public Guid InventoryId { get; private set; }
    public Guid ReservationId { get; private set; }
    public int Quantity { get; private set; }
    public Money UnitPrice { get; private set; }
    public Money Total => Money.Create(UnitPrice.Amount * Quantity, UnitPrice.Currency);
    public string IdempotencyKey { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public Guid? TicketId { get; private set; }
    public IReadOnlySet<string> CompletedSteps => _completedSteps;

    public static OrderAggregate Create(
        Guid orderId, Guid customerId, Guid inventoryId, Guid reservationId, int quantity,
        Money unitPrice, IdempotencyKey idempotencyKey, DateTimeOffset now)
    {
        if (orderId == Guid.Empty || customerId == Guid.Empty || inventoryId == Guid.Empty || reservationId == Guid.Empty)
            throw new DomainRuleViolationException("Order identifiers are required.");
        if (quantity <= 0) throw new DomainRuleViolationException("Order quantity must be positive.");

        var aggregate = new OrderAggregate();
        aggregate.Raise(new OrderCreated(Guid.NewGuid(), now, orderId, customerId, inventoryId,
            reservationId, quantity, unitPrice, idempotencyKey.Value));
        return aggregate;
    }

    public void CompleteStep(string step, DateTimeOffset now)
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Refunded)
            throw new DomainRuleViolationException("A terminal order cannot advance.");
        if (_completedSteps.Contains(step)) return;
        Raise(new OrderStepCompleted(Guid.NewGuid(), now, Id, step));
    }

    public void Complete(Guid ticketId, DateTimeOffset now)
    {
        if (Status == OrderStatus.Completed && TicketId == ticketId) return;
        if (Status == OrderStatus.Cancelled) throw new DomainRuleViolationException("Cancelled order cannot complete.");
        if (ticketId == Guid.Empty) throw new DomainRuleViolationException("Ticket ID is required.");
        Raise(new OrderCompleted(Guid.NewGuid(), now, Id, ticketId));
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        if (Status == OrderStatus.Cancelled) return;
        if (Status == OrderStatus.Completed) throw new DomainRuleViolationException("Completed order must use the refund flow.");
        Raise(new OrderCancelled(Guid.NewGuid(), now, Id, reason));
    }

    public void RequestRefund(string reason, DateTimeOffset now)
    {
        if (Status != OrderStatus.Completed) throw new DomainRuleViolationException("Only completed orders can be refunded.");
        Raise(new RefundRequested(Guid.NewGuid(), now, Id, reason));
    }

    public void MarkRefunded(DateTimeOffset now)
    {
        if (Status == OrderStatus.Refunded) return;
        if (Status != OrderStatus.Refunding) throw new DomainRuleViolationException("Refund was not requested.");
        Raise(new OrderRefunded(Guid.NewGuid(), now, Id, Total));
    }

    protected override void Apply(IDomainEvent @event)
    {
        switch (@event)
        {
            case OrderCreated e:
                Id = e.OrderId; CustomerId = e.CustomerId; InventoryId = e.InventoryId;
                ReservationId = e.ReservationId; Quantity = e.Quantity; UnitPrice = e.UnitPrice;
                IdempotencyKey = e.IdempotencyKey; Status = OrderStatus.Pending;
                break;
            case OrderStepCompleted e:
                _completedSteps.Add(e.Step); Status = OrderStatus.Processing;
                break;
            case OrderCompleted e:
                TicketId = e.TicketId; Status = OrderStatus.Completed;
                break;
            case OrderCancelled:
                Status = OrderStatus.Cancelled;
                break;
            case RefundRequested:
                Status = OrderStatus.Refunding;
                break;
            case OrderRefunded:
                Status = OrderStatus.Refunded;
                break;
        }
    }
}
