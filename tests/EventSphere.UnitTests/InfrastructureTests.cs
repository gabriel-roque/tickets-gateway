using EventSphere.Application;
using EventSphere.Domain;
using EventSphere.Infrastructure.Persistence;
using EventSphere.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventSphere.UnitTests;

public sealed class InfrastructureTests
{
    [Fact]
    public async Task Event_store_rejects_stale_expected_version()
    {
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<WriteDbContext>().UseInMemoryDatabase(databaseName).Options;
        var inventoryId = Guid.NewGuid();
        var metadata = Metadata();

        await using (var context = new WriteDbContext(options))
        {
            var store = new SqlEventStore(context);
            var aggregate = InventoryAggregate.Create(inventoryId, Guid.NewGuid(), "A", "1", 10,
                Money.Create(100), DateTimeOffset.UtcNow);
            await store.AppendAsync(Streams.Inventory(inventoryId), -1, aggregate.UncommittedEvents, metadata, default);
        }

        await using var firstContext = new WriteDbContext(options);
        await using var secondContext = new WriteDbContext(options);
        var firstRepository = new AggregateRepository(new SqlEventStore(firstContext));
        var secondRepository = new AggregateRepository(new SqlEventStore(secondContext));
        var first = await firstRepository.LoadAsync<InventoryAggregate>(Streams.Inventory(inventoryId), default);
        var second = await secondRepository.LoadAsync<InventoryAggregate>(Streams.Inventory(inventoryId), default);
        first.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow);
        second.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow);

        await firstRepository.SaveAsync(Streams.Inventory(inventoryId), first, metadata, default);
        await Assert.ThrowsAsync<ExpectedVersionException>(() =>
            secondRepository.SaveAsync(Streams.Inventory(inventoryId), second, metadata, default));
    }

    [Fact]
    public async Task Inbox_executes_duplicate_message_once()
    {
        var options = new DbContextOptionsBuilder<WriteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new WriteDbContext(options);
        var inbox = new SqlInbox(context, new SystemClock());
        var executions = 0;
        var message = new IntegrationMessage(Guid.NewGuid(), "test", "{}", "key",
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);

        var first = await inbox.ExecuteOnceAsync(message, _ => { executions++; return Task.CompletedTask; }, default);
        var second = await inbox.ExecuteOnceAsync(message, _ => { executions++; return Task.CompletedTask; }, default);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task Multi_stream_append_is_atomic_when_show_version_is_stale()
    {
        var options = new DbContextOptionsBuilder<WriteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new WriteDbContext(options);
        var store = new SqlEventStore(context);
        var showId = Guid.NewGuid();
        var firstInventoryId = Guid.NewGuid();
        var existingShow = ShowAggregate.For(showId);
        existingShow.RegisterInventory(firstInventoryId, DateTimeOffset.UtcNow);
        await store.AppendAsync(Streams.Show(showId), -1, existingShow.UncommittedEvents, Metadata(), default);

        var secondInventoryId = Guid.NewGuid();
        var secondInventory = InventoryAggregate.Create(secondInventoryId, showId, "A", "2", 10,
            Money.Create(100), DateTimeOffset.UtcNow);
        var staleShow = ShowAggregate.For(showId);
        staleShow.RegisterInventory(secondInventoryId, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ExpectedVersionException>(() => store.AppendManyAsync([
            new EventStreamAppend(Streams.Show(showId), -1, staleShow.UncommittedEvents, Metadata()),
            new EventStreamAppend(Streams.Inventory(secondInventoryId), -1,
                secondInventory.UncommittedEvents, Metadata())
        ], default));

        Assert.Empty(await store.ReadStreamAsync(Streams.Inventory(secondInventoryId), default));
    }

    [Fact]
    public async Task Cancellation_wins_optimistic_race_against_confirmation()
    {
        var options = new DbContextOptionsBuilder<WriteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var inventoryId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        await using (var seed = new WriteDbContext(options))
        {
            var repository = new AggregateRepository(new SqlEventStore(seed));
            var inventory = InventoryAggregate.Create(inventoryId, Guid.NewGuid(), "A", "1", 10,
                Money.Create(100), DateTimeOffset.UtcNow);
            inventory.Reserve(reservationId, Guid.NewGuid(), 1,
                DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow);
            await repository.SaveAsync(Streams.Inventory(inventoryId), inventory, Metadata(), default);
        }

        await using var cancellationContext = new WriteDbContext(options);
        await using var confirmationContext = new WriteDbContext(options);
        var cancellationRepository = new AggregateRepository(new SqlEventStore(cancellationContext));
        var confirmationRepository = new AggregateRepository(new SqlEventStore(confirmationContext));
        var cancelling = await cancellationRepository.LoadAsync<InventoryAggregate>(
            Streams.Inventory(inventoryId), default);
        var confirming = await confirmationRepository.LoadAsync<InventoryAggregate>(
            Streams.Inventory(inventoryId), default);
        cancelling.CloseSales("cancelled", DateTimeOffset.UtcNow);
        confirming.Confirm(reservationId, DateTimeOffset.UtcNow);

        await cancellationRepository.SaveAsync(Streams.Inventory(inventoryId), cancelling, Metadata(), default);
        await Assert.ThrowsAsync<ExpectedVersionException>(() => confirmationRepository.SaveAsync(
            Streams.Inventory(inventoryId), confirming, Metadata(), default));
    }

    [Fact]
    public async Task Pessimistic_seat_lock_is_idempotent_and_exclusive()
    {
        var options = new DbContextOptionsBuilder<WriteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var context = new WriteDbContext(options);
        var clock = new SystemClock();
        var service = new SqlSeatLockService(context, clock,
            Options.Create(new SeatLockOptions { TtlSeconds = 120 }));
        var ownerId = Guid.NewGuid();
        var reservation = Guid.NewGuid();
        var command = new LockSeatCommand(ownerId, Guid.NewGuid(), "A", "1", "10", reservation);

        await service.LockAsync(command, default);
        await service.LockAsync(command, default);

        await Assert.ThrowsAsync<DomainRuleViolationException>(() =>
            service.LockAsync(command with { ReservationId = Guid.NewGuid() }, default));

        var persisted = await context.SeatLocks.SingleAsync();
        Assert.Equal(ownerId, persisted.OwnerId);
        Assert.InRange(persisted.ExpiresAt, clock.UtcNow.AddSeconds(115), clock.UtcNow.AddSeconds(121));

        await Assert.ThrowsAsync<DomainRuleViolationException>(() =>
            service.ReleaseAsync(reservation, Guid.NewGuid(), false, default));
        await service.ReleaseAsync(reservation, ownerId, false, default);
        Assert.Empty(await context.SeatLocks.ToListAsync());
    }

    private static EventMetadata Metadata() => new(Guid.NewGuid(), Guid.NewGuid(), "test", "trace");
}
