namespace EventSphere.Domain;

public enum ReservationStatus { Active, Confirmed, Released }

public sealed record Reservation(
    Guid Id, Guid OrderId, int Quantity, DateTimeOffset ExpiresAt, ReservationStatus Status);

public sealed class InventoryAggregate : AggregateRoot
{
    private readonly Dictionary<Guid, Reservation> _reservations = [];

    public Guid ShowId { get; private set; }
    public string SectorId { get; private set; } = string.Empty;
    public string BucketId { get; private set; } = string.Empty;
    public int Capacity { get; private set; }
    public int Reserved { get; private set; }
    public int Confirmed { get; private set; }
    public bool SalesOpen { get; private set; }
    public Money UnitPrice { get; private set; } = new(0, "BRL");
    public int Available => Capacity - Reserved - Confirmed;
    public IReadOnlyDictionary<Guid, Reservation> Reservations => _reservations;

    public static InventoryAggregate Create(
        Guid inventoryId, Guid showId, string sectorId, string bucketId, int capacity, Money unitPrice,
        DateTimeOffset now)
    {
        if (inventoryId == Guid.Empty || showId == Guid.Empty) throw new DomainRuleViolationException("Inventory and show IDs are required.");
        if (string.IsNullOrWhiteSpace(sectorId) || string.IsNullOrWhiteSpace(bucketId))
            throw new DomainRuleViolationException("Sector and bucket are required.");
        if (capacity <= 0) throw new DomainRuleViolationException("Capacity must be greater than zero.");

        var aggregate = new InventoryAggregate();
        aggregate.Raise(new InventoryCreated(Guid.NewGuid(), now, inventoryId, showId, sectorId, bucketId, capacity, unitPrice));
        return aggregate;
    }

    public void Reserve(Guid reservationId, Guid orderId, int quantity, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (_reservations.TryGetValue(reservationId, out var existing))
        {
            if (existing.OrderId != orderId || existing.Quantity != quantity)
                throw new DomainRuleViolationException("Reservation idempotency conflict.");
            return;
        }
        if (!SalesOpen) throw new DomainRuleViolationException("Sales are closed for this inventory.");
        if (quantity <= 0) throw new DomainRuleViolationException("Reservation quantity must be positive.");
        if (expiresAt <= now) throw new DomainRuleViolationException("Reservation expiration must be in the future.");
        if (Available < quantity) throw new DomainRuleViolationException("Insufficient inventory.");

        Raise(new InventoryReserved(Guid.NewGuid(), now, Id, reservationId, orderId, quantity, expiresAt));
    }

    public void Confirm(Guid reservationId, DateTimeOffset now)
    {
        if (!SalesOpen) throw new DomainRuleViolationException("Sales are closed for this inventory.");
        var reservation = GetReservation(reservationId);
        if (reservation.Status == ReservationStatus.Confirmed) return;
        if (reservation.Status == ReservationStatus.Released)
            throw new DomainRuleViolationException("Released reservation cannot be confirmed.");
        if (reservation.ExpiresAt <= now) throw new DomainRuleViolationException("Expired reservation cannot be confirmed.");
        Raise(new InventoryConfirmed(Guid.NewGuid(), now, Id, reservationId, reservation.Quantity));
    }

    public void Release(Guid reservationId, string reason, DateTimeOffset now)
    {
        var reservation = GetReservation(reservationId);
        if (reservation.Status == ReservationStatus.Released) return;
        if (reservation.Status == ReservationStatus.Confirmed)
            throw new DomainRuleViolationException("Confirmed inventory must be refunded before release.");
        Raise(new InventoryReleased(Guid.NewGuid(), now, Id, reservationId, reservation.Quantity, reason));
    }

    public void ReturnConfirmed(Guid reservationId, string reason, DateTimeOffset now)
    {
        var reservation = GetReservation(reservationId);
        if (reservation.Status == ReservationStatus.Released) return;
        if (reservation.Status != ReservationStatus.Confirmed)
            throw new DomainRuleViolationException("Only confirmed inventory can be returned.");
        Raise(new InventoryReturned(Guid.NewGuid(), now, Id, reservationId, reservation.Quantity, reason));
    }

    public void Expire(Guid reservationId, DateTimeOffset now)
    {
        var reservation = GetReservation(reservationId);
        if (reservation.Status != ReservationStatus.Active || reservation.ExpiresAt > now) return;
        Raise(new InventoryReservationExpired(Guid.NewGuid(), now, Id, reservationId, reservation.Quantity));
    }

    public void CloseSales(string reason, DateTimeOffset now)
    {
        if (!SalesOpen) return;
        Raise(new InventorySalesClosed(Guid.NewGuid(), now, Id, reason));
    }

    private Reservation GetReservation(Guid id) => _reservations.TryGetValue(id, out var value)
        ? value
        : throw new DomainRuleViolationException("Reservation was not found.");

    protected override void Apply(IDomainEvent @event)
    {
        switch (@event)
        {
            case InventoryCreated e:
                Id = e.InventoryId; ShowId = e.ShowId; SectorId = e.SectorId; BucketId = e.BucketId;
                Capacity = e.Capacity; UnitPrice = e.UnitPrice;
                SalesOpen = true;
                break;
            case InventoryReserved e:
                _reservations[e.ReservationId] = new Reservation(e.ReservationId, e.OrderId, e.Quantity, e.ExpiresAt, ReservationStatus.Active);
                Reserved += e.Quantity;
                break;
            case InventoryReleased e:
                var released = _reservations[e.ReservationId];
                if (released.Status == ReservationStatus.Active) Reserved -= released.Quantity;
                _reservations[e.ReservationId] = released with { Status = ReservationStatus.Released };
                break;
            case InventoryConfirmed e:
                var confirmed = _reservations[e.ReservationId];
                Reserved -= confirmed.Quantity;
                Confirmed += confirmed.Quantity;
                _reservations[e.ReservationId] = confirmed with { Status = ReservationStatus.Confirmed };
                break;
            case InventoryReturned e:
                var returned = _reservations[e.ReservationId];
                Confirmed -= returned.Quantity;
                _reservations[e.ReservationId] = returned with { Status = ReservationStatus.Released };
                break;
            case InventoryReservationExpired e:
                var expired = _reservations[e.ReservationId];
                Reserved -= expired.Quantity;
                _reservations[e.ReservationId] = expired with { Status = ReservationStatus.Released };
                break;
            case InventorySalesClosed:
                SalesOpen = false;
                break;
        }
    }
}
