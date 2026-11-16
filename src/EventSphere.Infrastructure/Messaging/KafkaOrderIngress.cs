using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using EventSphere.Application;
using EventSphere.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSphere.Infrastructure.Messaging;

public sealed record OrderCommandEnvelope(CreateOrderCommand Command, EventMetadata Metadata);

public sealed class KafkaOrderIngress : IOrderIngress, IDisposable
{
    public const string Topic = "eventsphere.commands.orders";
    private readonly IProducer<string, string> _producer;

    public KafkaOrderIngress(string bootstrapServers) =>
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            CompressionType = CompressionType.Snappy,
            LingerMs = 2,
            BatchNumMessages = 10_000,
            QueueBufferingMaxMessages = 1_000_000,
            MessageSendMaxRetries = 10
        }).Build();

    public async Task<OperationAccepted> SubmitAsync(CreateOrderCommand command, EventMetadata metadata,
        CancellationToken cancellationToken)
    {
        var scopedKey = OrderIdempotency.ScopedKey(command);
        if (command.CustomerId == Guid.Empty || command.InventoryId == Guid.Empty || command.Quantity <= 0)
            throw new DomainRuleViolationException("Customer, inventory and positive quantity are required.");

        var prepared = command with
        {
            OrderId = StableGuid(scopedKey, "order"),
            ReservationId = StableGuid(scopedKey, "reservation"),
            SagaId = StableGuid(scopedKey, "saga")
        };
        var result = await _producer.ProduceAsync(Topic, new Message<string, string>
        {
            Key = prepared.InventoryId.ToString("N"),
            Value = JsonSerializer.Serialize(new OrderCommandEnvelope(prepared, metadata))
        }, cancellationToken);
        if (result.Status == PersistenceStatus.NotPersisted)
            throw new InvalidOperationException("Kafka did not persist the order command.");

        return new OperationAccepted(prepared.SagaId!.Value, prepared.OrderId!.Value,
            $"/api/v2/operations/{prepared.SagaId.Value}");
    }

    private static Guid StableGuid(string idempotencyKey, string purpose)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"eventsphere:{purpose}:{idempotencyKey}"));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    public void Dispose() => _producer.Dispose();
}

public sealed class KafkaOrderCommandConsumer(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaOrderCommandConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers is required for the order consumer.");
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = configuration["Kafka:OrderConsumerGroup"] ?? "eventsphere-order-commands-v1",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = false,
            MaxPollIntervalMs = 900_000
        }).Build();
        using var deadLetterProducer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All
        }).Build();
        consumer.Subscribe(KafkaOrderIngress.Topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? consumed = null;
            try
            {
                consumed = consumer.Consume(stoppingToken);
                var envelope = JsonSerializer.Deserialize<OrderCommandEnvelope>(consumed.Message.Value)
                    ?? throw new InvalidOperationException("Order command payload is empty.");
                await ProcessWithRetryAsync(envelope, stoppingToken);
                consumer.Commit(consumed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Order command failed at {Offset}", consumed?.TopicPartitionOffset);
                if (consumed is null) continue;
                await deadLetterProducer.ProduceAsync("eventsphere.commands.orders.dlq", new Message<string, string>
                {
                    Key = consumed.Message.Key,
                    Value = consumed.Message.Value,
                    Headers = new Headers
                    {
                        { "source-offset", Encoding.UTF8.GetBytes(consumed.TopicPartitionOffset.ToString()) },
                        { "error", Encoding.UTF8.GetBytes(exception.Message) }
                    }
                }, stoppingToken);
                consumer.Commit(consumed);
            }
        }
        consumer.Close();
    }

    private async Task ProcessWithRetryAsync(OrderCommandEnvelope envelope, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OrderCommandService>()
                    .CreateAsync(envelope.Command, envelope.Metadata, cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is not DomainRuleViolationException
                and not IdempotencyConflictException)
            {
                last = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(2_000, 25 * (1 << Math.Min(attempt, 6)))),
                    cancellationToken);
            }
        }
        throw new InvalidOperationException("Order command exhausted transient retries.", last);
    }
}
