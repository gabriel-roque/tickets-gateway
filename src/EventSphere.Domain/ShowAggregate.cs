namespace EventSphere.Domain;

public sealed class ShowAggregate : AggregateRoot
{
    private readonly HashSet<Guid> _inventoryIds = [];

    public bool IsCancelled { get; private set; }
    public IReadOnlySet<Guid> InventoryIds => _inventoryIds;

    public static ShowAggregate For(Guid showId)
    {
        if (showId == Guid.Empty) throw new DomainRuleViolationException("Show ID is required.");
        return new ShowAggregate { Id = showId };
    }

    public void RegisterInventory(Guid inventoryId, DateTimeOffset now)
    {
        if (IsCancelled) throw new DomainRuleViolationException("The show is cancelled.");
        if (inventoryId == Guid.Empty) throw new DomainRuleViolationException("Inventory ID is required.");
        if (_inventoryIds.Contains(inventoryId)) return;
        Raise(new ShowInventoryRegistered(Guid.NewGuid(), now, Id, inventoryId));
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        if (IsCancelled) return;
        Raise(new ShowCancelled(Guid.NewGuid(), now, Id,
            string.IsNullOrWhiteSpace(reason) ? "Show cancelled" : reason));
    }

    protected override void Apply(IDomainEvent @event)
    {
        switch (@event)
        {
            case ShowInventoryRegistered registered:
                Id = registered.ShowId;
                _inventoryIds.Add(registered.InventoryId);
                break;
            case ShowCancelled cancelled:
                Id = cancelled.ShowId;
                IsCancelled = true;
                break;
        }
    }
}
