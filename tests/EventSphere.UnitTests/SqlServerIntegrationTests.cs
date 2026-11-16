using System.Collections.Concurrent;
using EventSphere.Application;
using EventSphere.Infrastructure;
using EventSphere.Infrastructure.Persistence;
using EventSphere.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using DotNet.Testcontainers.Builders;
using Docker.DotNet.Models;

namespace EventSphere.UnitTests;

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
        .WithPassword("Integration-Only-123!")
        .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2304")
        .WithCreateParameterModifier(parameters =>
        {
            parameters.HostConfig ??= new HostConfig();
            parameters.HostConfig.Memory = 3L * 1024 * 1024 * 1024;
            parameters.HostConfig.NanoCPUs = 1_500_000_000;
        })
        .WithAutoRemove(true)
        .WithCleanUp(false)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(1433))
        .Build();

    public string ConnectionString => _container.GetConnectionString();
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Exception? lastError = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (SqlException exception)
            {
                lastError = exception;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
        throw new InvalidOperationException("SQL Server did not become login-ready in time.", lastError);
    }
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerIntegrationTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Scoped_idempotency_migration_preserves_replays_from_the_previous_schema()
    {
        var databaseName = $"eventsphere_upgrade_{Guid.NewGuid():N}";
        var masterBuilder = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
        }

        var databaseBuilder = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = databaseName };
        var provider = CreateProvider(databaseBuilder.ConnectionString);
        try
        {
            var rawKey = "pre-scope-key-0001";
            var customerId = Guid.NewGuid();
            var inventoryId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var sagaId = Guid.NewGuid();
            await using (var oldSchema = provider.CreateAsyncScope())
            {
                var database = oldSchema.ServiceProvider.GetRequiredService<WriteDbContext>();
                await database.GetService<IMigrator>().MigrateAsync("20261112222252_AddFaultInjection");
                var now = DateTimeOffset.UtcNow;
                await database.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO [Sagas]
                        ([Id],[OrderId],[CustomerId],[InventoryId],[ReservationId],[Quantity],[UnitPrice],[Currency],
                         [TicketId],[Step],[Status],[Attempts],[LastError],[CreatedAt],[UpdatedAt],[NextAttemptAt],
                         [LeaseUntil],[Version])
                    VALUES
                        ({sagaId},{orderId},{customerId},{inventoryId},{Guid.NewGuid()},1,10.0,{"BRL"},
                         NULL,{(int)PurchaseStep.Completed},{(int)SagaStatus.Completed},0,NULL,{now},{now},{now},
                         NULL,0);
                    INSERT INTO [Idempotency] ([Key],[OrderId],[SagaId],[CreatedAt])
                    VALUES ({rawKey},{orderId},{sagaId},{now});
                    """);
                await database.Database.MigrateAsync();
            }

            await using var replay = provider.CreateAsyncScope();
            var databaseAfter = replay.ServiceProvider.GetRequiredService<WriteDbContext>();
            var migrated = await databaseAfter.Idempotency.SingleAsync(x => x.OrderId == orderId);
            Assert.Equal(rawKey, migrated.RawKey);
            Assert.Equal(string.Empty, migrated.Fingerprint);
            var accepted = await replay.ServiceProvider.GetRequiredService<OrderCommandService>().CreateAsync(
                new CreateOrderCommand(customerId, inventoryId, 1, rawKey),
                new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "upgrade-test", "trace"), default);
            Assert.Equal(orderId, accepted.OrderId);
            Assert.Equal(sagaId, accepted.OperationId);
        }
        finally
        {
            await provider.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(masterBuilder.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Migrations_idempotency_and_multi_worker_outbox_are_safe_on_sql_server()
    {
        RecordingPublisher.Reset();
        var providers = Enumerable.Range(0, 4).Select(_ => CreateProvider()).ToArray();
        try
        {
            await using (var scope = providers[0].CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<WriteDbContext>().Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<ReadDbContext>().Database.MigrateAsync();
            }

            var inventoryId = Guid.NewGuid();
            var metadata = new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "integration-test", "trace");
            await using (var scope = providers[0].CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
                    new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", "sql", 100, 25m), metadata, default);

            var command = new CreateOrderCommand(Guid.NewGuid(), inventoryId, 1, "sql-concurrent-idempotency");
            var submissions = Enumerable.Range(0, 40).Select(async index =>
            {
                await using var scope = providers[index % providers.Length].CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<OrderCommandService>()
                    .CreateAsync(command, metadata, default);
            });
            var results = await Task.WhenAll(submissions);
            Assert.Single(results.Select(x => x.OrderId).Distinct());
            Assert.Single(results.Select(x => x.OperationId).Distinct());

            var claims = await Task.WhenAll(providers.Select(async provider =>
            {
                await using var scope = provider.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<ISagaRepository>()
                    .ClaimRunnableAsync(100, TimeSpan.FromSeconds(30), default);
            }));
            Assert.Single(claims.SelectMany(x => x));

            await Task.WhenAll(providers.Select(provider =>
                provider.GetRequiredService<OutboxDispatcher>().RunBatchAsync(100, default)));

            Assert.NotEmpty(RecordingPublisher.Deliveries);
            Assert.All(RecordingPublisher.Deliveries.Values, count => Assert.Equal(1, count));
            await using var verify = providers[0].CreateAsyncScope();
            var database = verify.ServiceProvider.GetRequiredService<WriteDbContext>();
            Assert.Equal(0, await database.Outbox.CountAsync(x => x.PublishedAt == null));
        }
        finally
        {
            foreach (var provider in providers) await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Incremental_projection_does_not_skip_concurrent_commits()
    {
        var providers = Enumerable.Range(0, 4).Select(_ => CreateProvider()).ToArray();
        var inventoryIds = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        using var projectionCancellation = new CancellationTokenSource();
        try
        {
            await using (var scope = providers[0].CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<WriteDbContext>().Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<ReadDbContext>().Database.MigrateAsync();
            }

            var projector = Task.Run(async () =>
            {
                while (!projectionCancellation.IsCancellationRequested)
                {
                    await using var scope = providers[0].CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ProjectionEngine>()
                        .RunBatchAsync(5, projectionCancellation.Token);
                    await Task.Yield();
                }
            });

            var appends = inventoryIds.Select(async (inventoryId, index) =>
            {
                await using var scope = providers[index % providers.Length].CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<InventoryCommandService>().CreateAsync(
                    new CreateInventoryCommand(inventoryId, Guid.NewGuid(), "floor", $"commit-{index}", 1, 10m),
                    new EventMetadata(Guid.NewGuid(), Guid.NewGuid(), "commit-order-test", "trace"), default);
            });
            await Task.WhenAll(appends);
            projectionCancellation.Cancel();
            try { await projector; }
            catch (OperationCanceledException) { }

            while (true)
            {
                await using var scope = providers[0].CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<ProjectionEngine>()
                        .RunBatchAsync(100, default) == 0)
                    break;
            }

            await using var verify = providers[0].CreateAsyncScope();
            var read = verify.ServiceProvider.GetRequiredService<ReadDbContext>();
            Assert.Equal(inventoryIds.Length,
                await read.Inventories.CountAsync(x => inventoryIds.Contains(x.InventoryId)));
            Assert.Equal(inventoryIds.Length,
                await read.Inventories.Where(x => inventoryIds.Contains(x.InventoryId)).SumAsync(x => x.Capacity));
        }
        finally
        {
            projectionCancellation.Cancel();
            foreach (var provider in providers) await provider.DisposeAsync();
        }
    }

    private ServiceProvider CreateProvider(string? connectionString = null)
    {
        connectionString ??= fixture.ConnectionString;
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:WriteDb"] = connectionString,
            ["ConnectionStrings:ReadDb"] = connectionString
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEventSphere(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.RemoveAll<IMessagePublisher>();
        services.AddSingleton<IMessagePublisher, RecordingPublisher>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private sealed class RecordingPublisher : IMessagePublisher
    {
        public static ConcurrentDictionary<Guid, int> Deliveries { get; } = new();
        public static void Reset() => Deliveries.Clear();
        public Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken)
        {
            Deliveries.AddOrUpdate(message.MessageId, 1, (_, count) => count + 1);
            return Task.CompletedTask;
        }
    }
}
