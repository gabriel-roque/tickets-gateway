using EventSphere.Application;
using EventSphere.Domain;
using EventSphere.Infrastructure;
using EventSphere.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventSphere.UnitTests;

public sealed class SagaAndProjectionTests
{
    [Fact]
    public async Task Purchase_saga_completes_and_projection_can_be_rebuilt()
    {
        await using var provider = CreateProvider();
        var inventoryId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var metadata = new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace");
        OperationAccepted accepted;

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
                new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", "bucket-1", 10, 125m), metadata, default);
            accepted = await scope.ServiceProvider.GetRequiredService<OrderCommandService>().CreateAsync(
                new CreateOrderCommand(customerId, inventoryId, 2, "idempotency-0001"), metadata, default);
        }

        for (var i = 0; i < 10; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PurchaseSagaOrchestrator>()
                .ProcessAsync(accepted.OperationId, default);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var saga = await scope.ServiceProvider.GetRequiredService<ISagaRepository>()
                .GetAsync(accepted.OperationId, default);
            Assert.NotNull(saga);
            Assert.Equal(SagaStatus.Completed, saga.Status);

            var projection = scope.ServiceProvider.GetRequiredService<ProjectionEngine>();
            await projection.RebuildAsync(default);
            var readModels = scope.ServiceProvider.GetRequiredService<IReadModelStore>();
            var order = await readModels.GetOrderAsync(accepted.OrderId, default);
            var inventory = await readModels.GetInventoryAsync(inventoryId, default);
            var ticket = await readModels.GetTicketAsync(saga.TicketId!.Value, default);

            Assert.Equal("Completed", order!.Status);
            Assert.Equal(2, inventory!.Confirmed);
            Assert.Equal(8, inventory.Available);
            Assert.Equal(customerId, ticket!.OwnerId);
        }

        Guid refundWorkflow;
        await using (var scope = provider.CreateAsyncScope())
            refundWorkflow = await scope.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                .StartRefundAsync(accepted.OrderId, "test refund", default);
        for (var i = 0; i < 7; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                .ProcessAsync(refundWorkflow, default);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var workflow = await scope.ServiceProvider.GetRequiredService<ILifecycleWorkflowRepository>()
                .GetAsync(refundWorkflow, default);
            Assert.Equal(LifecycleWorkflowStatus.Completed, workflow!.Status);
            await scope.ServiceProvider.GetRequiredService<ProjectionEngine>().RebuildAsync(default);
            var order = await scope.ServiceProvider.GetRequiredService<IReadModelStore>()
                .GetOrderAsync(accepted.OrderId, default);
            Assert.Equal("Refunded", order!.Status);
            var inventory = await scope.ServiceProvider.GetRequiredService<IReadModelStore>()
                .GetInventoryAsync(inventoryId, default);
            Assert.Equal(0, inventory!.Confirmed);
            Assert.Equal(10, inventory.Available);
        }
    }

    [Fact]
    public async Task Idempotency_key_returns_same_order()
    {
        await using var provider = CreateProvider();
        var inventoryId = Guid.NewGuid();
        var command = new CreateOrderCommand(Guid.NewGuid(), inventoryId, 1, "same-key-000001");
        var metadata = new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace");

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
            new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", "idempotency", 10, 10m), metadata, default);
        var service = scope.ServiceProvider.GetRequiredService<OrderCommandService>();
        var first = await service.CreateAsync(command, metadata, default);
        var second = await service.CreateAsync(command, metadata, default);

        Assert.Equal(first.OrderId, second.OrderId);
    }

    [Fact]
    public async Task Idempotency_key_is_scoped_to_customer()
    {
        await using var provider = CreateProvider();
        var inventoryId = Guid.NewGuid();
        var metadata = new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace");
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
            new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", "customer-scope", 10, 10m),
            metadata, default);
        var service = scope.ServiceProvider.GetRequiredService<OrderCommandService>();

        var first = await service.CreateAsync(
            new CreateOrderCommand(Guid.NewGuid(), inventoryId, 1, "shared-key-0001"), metadata, default);
        var second = await service.CreateAsync(
            new CreateOrderCommand(Guid.NewGuid(), inventoryId, 1, "shared-key-0001"), metadata, default);

        Assert.NotEqual(first.OrderId, second.OrderId);
        Assert.NotEqual(first.OperationId, second.OperationId);
    }

    [Fact]
    public async Task Reusing_idempotency_key_with_different_payload_is_rejected()
    {
        await using var provider = CreateProvider();
        var inventoryId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var metadata = new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace");
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
            new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", "payload-conflict", 10, 10m),
            metadata, default);
        var service = scope.ServiceProvider.GetRequiredService<OrderCommandService>();
        await service.CreateAsync(
            new CreateOrderCommand(customerId, inventoryId, 1, "payload-key-0001"), metadata, default);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.CreateAsync(
            new CreateOrderCommand(customerId, inventoryId, 2, "payload-key-0001"), metadata, default));
    }

    [Fact]
    public async Task Legacy_raw_idempotency_row_remains_replay_safe_after_scoped_key_upgrade()
    {
        await using var provider = CreateProvider();
        var rawKey = "legacy-key-0001";
        var customerId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var oldOrderId = Guid.NewGuid();
        var oldSagaId = Guid.NewGuid();
        await using (var seed = provider.CreateAsyncScope())
        {
            var database = seed.ServiceProvider.GetRequiredService<WriteDbContext>();
            database.Sagas.Add(new SagaRecord
            {
                Id = oldSagaId,
                OrderId = oldOrderId,
                CustomerId = customerId,
                InventoryId = inventoryId,
                ReservationId = Guid.NewGuid(),
                Quantity = 1,
                UnitPrice = 10m,
                Currency = "BRL",
                Status = (int)SagaStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                NextAttemptAt = DateTimeOffset.UtcNow
            });
            database.Idempotency.Add(new IdempotencyRecord
            {
                Key = rawKey,
                RawKey = rawKey,
                Fingerprint = string.Empty,
                OrderId = oldOrderId,
                SagaId = oldSagaId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await database.SaveChangesAsync();
        }

        await using var replay = provider.CreateAsyncScope();
        var service = replay.ServiceProvider.GetRequiredService<OrderCommandService>();
        var metadata = new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace");
        var accepted = await service.CreateAsync(
            new CreateOrderCommand(customerId, inventoryId, 1, rawKey), metadata, default);
        Assert.Equal(oldOrderId, accepted.OrderId);
        Assert.Equal(oldSagaId, accepted.OperationId);
        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.CreateAsync(
            new CreateOrderCommand(customerId, inventoryId, 2, rawKey), metadata, default));
    }

    [Fact]
    public async Task Concurrent_idempotency_requests_converge_to_one_order()
    {
        await using var provider = CreateProvider();
        var inventoryId = Guid.NewGuid();
        var command = new CreateOrderCommand(Guid.NewGuid(), inventoryId, 1, "parallel-key-0001");
        await using (var setup = provider.CreateAsyncScope())
            await setup.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
                new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", "parallel", 10, 10m),
                new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace"), default);
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OrderCommandService>().CreateAsync(command,
                new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace"), default);
        });

        var results = await Task.WhenAll(tasks);

        Assert.Single(results.Select(x => x.OrderId).Distinct());
        Assert.Single(results.Select(x => x.OperationId).Distinct());
    }

    [Fact]
    public async Task Concurrent_idempotency_requests_with_different_payload_do_not_converge_silently()
    {
        await using var provider = CreateProvider();
        var inventoryId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using (var setup = provider.CreateAsyncScope())
            await setup.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
                new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", "parallel-conflict", 10, 10m),
                new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace"), default);

        async Task<object> SubmitAsync(int quantity)
        {
            await using var scope = provider.CreateAsyncScope();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<OrderCommandService>().CreateAsync(
                    new CreateOrderCommand(customerId, inventoryId, quantity, "parallel-conflict-key"),
                    new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace"), default);
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var results = await Task.WhenAll(SubmitAsync(1), SubmitAsync(2));

        Assert.Single(results.OfType<OperationAccepted>());
        Assert.Single(results.OfType<IdempotencyConflictException>());
    }

    [Fact]
    public async Task Failure_after_inventory_confirmation_compensates_all_effects()
    {
        await using var provider = CreateProvider(services =>
        {
            services.RemoveAll<INotificationParticipant>();
            services.AddScoped<INotificationParticipant, FailingNotificationParticipant>();
        });
        var inventoryId = Guid.NewGuid();
        OperationAccepted accepted;
        await using (var scope = provider.CreateAsyncScope())
        {
            var metadata = new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace");
            await scope.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
                new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", "failure", 1, 10m), metadata, default);
            accepted = await scope.ServiceProvider.GetRequiredService<OrderCommandService>().CreateAsync(
                new CreateOrderCommand(Guid.NewGuid(), inventoryId, 1, "failure-after-confirm"), metadata, default);
        }

        for (var attempt = 0; attempt < 25; attempt++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PurchaseSagaOrchestrator>()
                .ProcessAsync(accepted.OperationId, default);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var saga = await scope.ServiceProvider.GetRequiredService<ISagaRepository>().GetAsync(accepted.OperationId, default);
            Assert.Equal(SagaStatus.Compensated, saga!.Status);
            var inventory = await scope.ServiceProvider.GetRequiredService<AggregateRepository>()
                .LoadAsync<InventoryAggregate>(Streams.Inventory(inventoryId), default);
            Assert.Equal(0, inventory.Confirmed);
            Assert.Equal(1, inventory.Available);
            var database = scope.ServiceProvider.GetRequiredService<WriteDbContext>();
            Assert.Equal("Cancelled", (await database.Payments.SingleAsync()).Status);
            Assert.Equal("Reversed", (await database.Settlements.SingleAsync()).Status);
        }
    }

    [Fact]
    public async Task Refund_is_rejected_while_purchase_saga_is_still_running()
    {
        await using var provider = CreateProvider();
        var inventoryId = Guid.NewGuid();
        OperationAccepted accepted;
        await using (var setup = provider.CreateAsyncScope())
        {
            var metadata = new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace");
            await setup.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
                new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", "pending-refund", 10, 10m),
                metadata, default);
            accepted = await setup.ServiceProvider.GetRequiredService<OrderCommandService>().CreateAsync(
                new CreateOrderCommand(Guid.NewGuid(), inventoryId, 1, "pending-refund-key"),
                metadata, default);
        }

        await using var attempt = provider.CreateAsyncScope();
        await Assert.ThrowsAsync<DomainRuleViolationException>(() =>
            attempt.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                .StartRefundAsync(accepted.OrderId, "too early", default));
        Assert.Null(await attempt.ServiceProvider.GetRequiredService<ILifecycleWorkflowRepository>()
            .GetAsync(DeterministicWorkflowId($"refund:{accepted.OrderId:N}"), default));
    }

    [Fact]
    public async Task Event_cancellation_closes_every_inventory_bucket()
    {
        await using var provider = CreateProvider();
        var showId = Guid.NewGuid();
        var inventoryIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using (var scope = provider.CreateAsyncScope())
        {
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryCommandService>();
            foreach (var id in inventoryIds)
                await inventory.CreateAsync(new CreateInventoryCommand(id, showId, "floor", id.ToString("N"), 10, 20m),
                    new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace"), default);
            var workflow = await scope.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                .StartEventCancellationAsync(showId, default);
            await scope.ServiceProvider.GetRequiredService<LifecycleSagaService>().ProcessAsync(workflow, default);
        }

        await using var verify = provider.CreateAsyncScope();
        foreach (var id in inventoryIds)
        {
            var aggregate = await verify.ServiceProvider.GetRequiredService<AggregateRepository>()
                .LoadAsync<InventoryAggregate>(Streams.Inventory(id), default);
            Assert.False(aggregate.SalesOpen);
        }
    }

    [Fact]
    public async Task Transfer_saga_recovers_when_ticket_changed_before_workflow_checkpoint()
    {
        await using var provider = CreateProvider();
        var ticketId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var newOwnerId = Guid.NewGuid();
        Guid workflowId;

        await using (var setup = provider.CreateAsyncScope())
        {
            var repository = setup.ServiceProvider.GetRequiredService<AggregateRepository>();
            var ticket = TicketAggregate.Issue(ticketId, orderId, ownerId, "qr-original", DateTimeOffset.UtcNow);
            await repository.SaveAsync(Streams.Ticket(ticketId), ticket,
                new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace"), default);
            workflowId = await setup.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                .StartTransferAsync(ticketId, newOwnerId, ownerId, false, default);
        }

        // Simulates a crash after the aggregate append but before the workflow step is persisted.
        await using (var crashedAttempt = provider.CreateAsyncScope())
        {
            var repository = crashedAttempt.ServiceProvider.GetRequiredService<AggregateRepository>();
            var ticket = await repository.LoadAsync<TicketAggregate>(Streams.Ticket(ticketId), default);
            ticket.Transfer(newOwnerId, "qr-after-transfer", DateTimeOffset.UtcNow);
            await repository.SaveAsync(Streams.Ticket(ticketId), ticket,
                new EventMetadata(workflowId, workflowId, "workflow-crash", "trace"), default);
        }


        var finalOwnerId = Guid.NewGuid();
        await using (var laterTransfer = provider.CreateAsyncScope())
        {
            var repository = laterTransfer.ServiceProvider.GetRequiredService<AggregateRepository>();
            var ticket = await repository.LoadAsync<TicketAggregate>(Streams.Ticket(ticketId), default);
            ticket.Transfer(finalOwnerId, "qr-later-transfer", DateTimeOffset.UtcNow);
            await repository.SaveAsync(Streams.Ticket(ticketId), ticket,
                new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "later-transfer", "trace"), default);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                .ProcessAsync(workflowId, default);
        }

        await using var verify = provider.CreateAsyncScope();
        var workflow = await verify.ServiceProvider.GetRequiredService<ILifecycleWorkflowRepository>()
            .GetAsync(workflowId, default);
        var transferred = await verify.ServiceProvider.GetRequiredService<AggregateRepository>()
            .LoadAsync<TicketAggregate>(Streams.Ticket(ticketId), default);
        Assert.Equal(LifecycleWorkflowStatus.Completed, workflow!.Status);
        Assert.Equal(ownerId, workflow.InitiatorId);
        Assert.Equal(finalOwnerId, transferred.OwnerId);
    }

    [Fact]
    public async Task Stale_concurrent_transfer_cannot_move_ticket_from_a_new_owner()
    {
        await using var provider = CreateProvider();
        var ticketId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var firstNewOwner = Guid.NewGuid();
        var staleNewOwner = Guid.NewGuid();
        Guid firstWorkflow;
        Guid staleWorkflow;
        await using (var setup = provider.CreateAsyncScope())
        {
            var repository = setup.ServiceProvider.GetRequiredService<AggregateRepository>();
            var ticket = TicketAggregate.Issue(ticketId, Guid.NewGuid(), ownerId, "qr-original", DateTimeOffset.UtcNow);
            await repository.SaveAsync(Streams.Ticket(ticketId), ticket,
                new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace"), default);
            var service = setup.ServiceProvider.GetRequiredService<LifecycleSagaService>();
            firstWorkflow = await service.StartTransferAsync(ticketId, firstNewOwner, ownerId, false, default);
            staleWorkflow = await service.StartTransferAsync(ticketId, staleNewOwner, ownerId, false, default);
        }

        await using (var first = provider.CreateAsyncScope())
            await first.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                .ProcessAsync(firstWorkflow, default);
        await using (var stale = provider.CreateAsyncScope())
            await stale.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                .ProcessAsync(staleWorkflow, default);

        await using var verify = provider.CreateAsyncScope();
        var ticketAfterRace = await verify.ServiceProvider.GetRequiredService<AggregateRepository>()
            .LoadAsync<TicketAggregate>(Streams.Ticket(ticketId), default);
        var rejected = await verify.ServiceProvider.GetRequiredService<ILifecycleWorkflowRepository>()
            .GetAsync(staleWorkflow, default);
        Assert.Equal(firstNewOwner, ticketAfterRace.OwnerId);
        Assert.Equal(LifecycleWorkflowStatus.Failed, rejected!.Status);
    }

    [Fact]
    public async Task Sequential_transfers_each_emit_an_idempotent_notification()
    {
        await using var provider = CreateProvider();
        var ticketId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var ownerC = Guid.NewGuid();
        await using (var setup = provider.CreateAsyncScope())
        {
            var ticket = TicketAggregate.Issue(ticketId, orderId, ownerA, "qr-a", DateTimeOffset.UtcNow);
            await setup.ServiceProvider.GetRequiredService<AggregateRepository>().SaveAsync(
                Streams.Ticket(ticketId), ticket,
                new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace"), default);
        }

        async Task TransferAsync(Guid actor, Guid target)
        {
            Guid workflowId;
            await using (var start = provider.CreateAsyncScope())
                workflowId = await start.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                    .StartTransferAsync(ticketId, target, actor, false, default);
            for (var step = 0; step < 2; step++)
            {
                await using var process = provider.CreateAsyncScope();
                await process.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                    .ProcessAsync(workflowId, default);
            }
        }

        await TransferAsync(ownerA, ownerB);
        await TransferAsync(ownerB, ownerC);

        await using var verify = provider.CreateAsyncScope();
        var notifications = await verify.ServiceProvider.GetRequiredService<WriteDbContext>()
            .Notifications.Where(x => x.OrderId == orderId).ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Equal(2, notifications.Select(x => x.Template).Distinct().Count());
    }

    [Fact]
    public async Task Concurrent_transfer_workflow_creation_converges_to_one_durable_workflow()
    {
        await using var provider = CreateProvider();
        var ticketId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var newOwnerId = Guid.NewGuid();
        await using (var setup = provider.CreateAsyncScope())
        {
            var ticket = TicketAggregate.Issue(
                ticketId, Guid.NewGuid(), ownerId, "qr-original", DateTimeOffset.UtcNow);
            await setup.ServiceProvider.GetRequiredService<AggregateRepository>().SaveAsync(
                Streams.Ticket(ticketId), ticket,
                new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "test", "trace"), default);
        }

        var starts = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<LifecycleSagaService>()
                .StartTransferAsync(ticketId, newOwnerId, ownerId, false, default);
        });
        var workflowIds = await Task.WhenAll(starts);

        Assert.Single(workflowIds.Distinct());
        await using var verify = provider.CreateAsyncScope();
        Assert.Equal(1, await verify.ServiceProvider.GetRequiredService<WriteDbContext>()
            .LifecycleWorkflows.CountAsync());
    }

    private static Guid DeterministicWorkflowId(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventSphere(new ConfigurationBuilder().Build());
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<WriteDbContext>().Database.EnsureDeleted();
        scope.ServiceProvider.GetRequiredService<WriteDbContext>().Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<ReadDbContext>().Database.EnsureDeleted();
        scope.ServiceProvider.GetRequiredService<ReadDbContext>().Database.EnsureCreated();
        return provider;
    }

    private sealed class FailingNotificationParticipant : INotificationParticipant
    {
        public Task NotifyOrderCompletedAsync(PurchaseSagaState saga, CancellationToken cancellationToken) =>
            throw new HttpRequestException("notification provider unavailable");

        public Task NotifyAsync(Guid orderId, string template, CancellationToken cancellationToken) =>
            throw new HttpRequestException("notification provider unavailable");
    }
}
