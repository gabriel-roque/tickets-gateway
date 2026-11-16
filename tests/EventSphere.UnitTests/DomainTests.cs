using EventSphere.Domain;

namespace EventSphere.UnitTests;

public sealed class DomainTests
{
    [Fact]
    public void Purchase_money_must_be_positive() =>
        Assert.Throws<DomainRuleViolationException>(() => Money.Create(0, "BRL"));
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Inventory_never_allows_overselling()
    {
        var inventory = InventoryAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), "floor", "A", 2,
            Money.Create(100), Now);
        inventory.MarkChangesAsCommitted();
        inventory.Reserve(Guid.NewGuid(), Guid.NewGuid(), 2, Now.AddMinutes(5), Now);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            inventory.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, Now.AddMinutes(5), Now));

        Assert.Equal("Insufficient inventory.", exception.Message);
        Assert.Equal(0, inventory.Available);
    }

    [Fact]
    public void Reservation_operations_are_idempotent()
    {
        var inventory = InventoryAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), "floor", "A", 5,
            Money.Create(100), Now);
        inventory.MarkChangesAsCommitted();
        var reservationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        inventory.Reserve(reservationId, orderId, 1, Now.AddMinutes(5), Now);
        inventory.Reserve(reservationId, orderId, 1, Now.AddMinutes(5), Now);
        inventory.Confirm(reservationId, Now.AddMinutes(1));
        inventory.Confirm(reservationId, Now.AddMinutes(1));

        Assert.Equal(0, inventory.Reserved);
        Assert.Equal(1, inventory.Confirmed);
        Assert.Equal(4, inventory.Available);
    }

    [Fact]
    public void Cancelled_show_rejects_new_inventory_and_confirmation()
    {
        var show = ShowAggregate.For(Guid.NewGuid());
        show.Cancel("cancelled", Now);
        Assert.Throws<DomainRuleViolationException>(() => show.RegisterInventory(Guid.NewGuid(), Now));

        var inventory = InventoryAggregate.Create(Guid.NewGuid(), show.Id, "floor", "A", 5,
            Money.Create(100), Now);
        var reservationId = Guid.NewGuid();
        inventory.Reserve(reservationId, Guid.NewGuid(), 1, Now.AddMinutes(5), Now);
        inventory.CloseSales("cancelled", Now);

        Assert.Throws<DomainRuleViolationException>(() => inventory.Confirm(reservationId, Now));
    }

    [Fact]
    public void Checked_in_ticket_cannot_be_transferred()
    {
        var ticket = TicketAggregate.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "qr-a", Now);
        ticket.CheckIn("gate-1", Now);

        Assert.Throws<DomainRuleViolationException>(() =>
            ticket.Transfer(Guid.NewGuid(), "qr-b", Now));
    }

    [Fact]
    public void Completed_order_uses_refund_instead_of_cancel()
    {
        var order = NewOrder();
        order.Complete(Guid.NewGuid(), Now);

        Assert.Throws<DomainRuleViolationException>(() => order.Cancel("test", Now));
        order.RequestRefund("customer request", Now);
        order.MarkRefunded(Now);

        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    private static OrderAggregate NewOrder() => OrderAggregate.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
        Money.Create(100), IdempotencyKey.Create("order-key-0001"), Now);
}
