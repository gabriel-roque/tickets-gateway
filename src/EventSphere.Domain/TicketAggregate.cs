namespace EventSphere.Domain;

public enum TicketStatus { Active, CheckedIn, Invalidated }

public sealed class TicketAggregate : AggregateRoot
{
    public Guid OrderId { get; private set; }
    public Guid OwnerId { get; private set; }
    public string QrCode { get; private set; } = string.Empty;
    public TicketStatus Status { get; private set; }

    public static TicketAggregate Issue(Guid ticketId, Guid orderId, Guid ownerId, string qrCode, DateTimeOffset now)
    {
        if (ticketId == Guid.Empty || orderId == Guid.Empty || ownerId == Guid.Empty || string.IsNullOrWhiteSpace(qrCode))
            throw new DomainRuleViolationException("Ticket issuance data is incomplete.");
        var aggregate = new TicketAggregate();
        aggregate.Raise(new TicketIssued(Guid.NewGuid(), now, ticketId, orderId, ownerId, qrCode));
        return aggregate;
    }

    public void Transfer(Guid newOwnerId, string newQrCode, DateTimeOffset now)
    {
        EnsureActive();
        if (newOwnerId == Guid.Empty || newOwnerId == OwnerId) throw new DomainRuleViolationException("A different new owner is required.");
        if (string.IsNullOrWhiteSpace(newQrCode)) throw new DomainRuleViolationException("A new QR code is required.");
        Raise(new TicketTransferred(Guid.NewGuid(), now, Id, OwnerId, newOwnerId, newQrCode));
    }

    public void CheckIn(string gateId, DateTimeOffset now)
    {
        if (Status == TicketStatus.CheckedIn) return;
        EnsureActive();
        if (string.IsNullOrWhiteSpace(gateId)) throw new DomainRuleViolationException("Gate ID is required.");
        Raise(new TicketCheckedIn(Guid.NewGuid(), now, Id, gateId));
    }

    public void Invalidate(string reason, DateTimeOffset now)
    {
        if (Status == TicketStatus.Invalidated) return;
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleViolationException("Invalidation reason is required.");
        Raise(new TicketInvalidated(Guid.NewGuid(), now, Id, reason));
    }

    private void EnsureActive()
    {
        if (Status != TicketStatus.Active) throw new DomainRuleViolationException("Ticket is not active.");
    }

    protected override void Apply(IDomainEvent @event)
    {
        switch (@event)
        {
            case TicketIssued e:
                Id = e.TicketId; OrderId = e.OrderId; OwnerId = e.OwnerId; QrCode = e.QrCode; Status = TicketStatus.Active;
                break;
            case TicketTransferred e:
                OwnerId = e.NewOwnerId; QrCode = e.NewQrCode;
                break;
            case TicketCheckedIn:
                Status = TicketStatus.CheckedIn;
                break;
            case TicketInvalidated:
                Status = TicketStatus.Invalidated;
                break;
        }
    }
}
