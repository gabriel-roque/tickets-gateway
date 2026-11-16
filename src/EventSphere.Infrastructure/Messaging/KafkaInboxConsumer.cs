using System.Text;
using Confluent.Kafka;
using EventSphere.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventSphere.Infrastructure.Messaging;

public sealed class KafkaInboxConsumer(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaInboxConsumer> logger) : BackgroundService
{
    private readonly Dictionary<TopicPartitionOffset, int> _failures = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers is required for the integration consumer.");
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = configuration["Kafka:ConsumerGroup"] ?? "eventsphere-inbox-v1",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = true
        }).Build();
        using var deadLetterProducer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All
        }).Build();
        consumer.Subscribe("eventsphere.events");

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? consumed = null;
            try
            {
                consumed = consumer.Consume(stoppingToken);
                var message = ToIntegrationMessage(consumed.Message);
                await using var scope = scopeFactory.CreateAsyncScope();
                var inbox = scope.ServiceProvider.GetRequiredService<IInbox>();
                await inbox.ExecuteOnceAsync(message, _ => Task.CompletedTask, stoppingToken);
                consumer.Commit(consumed);
                _failures.Remove(consumed.TopicPartitionOffset);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Kafka inbox processing failed at {Offset}", consumed?.TopicPartitionOffset);
                if (consumed is null) continue;
                var count = _failures.GetValueOrDefault(consumed.TopicPartitionOffset) + 1;
                _failures[consumed.TopicPartitionOffset] = count;
                if (count < 5) continue;

                await deadLetterProducer.ProduceAsync("eventsphere.events.dlq", new Message<string, string>
                {
                    Key = consumed.Message.Key,
                    Value = consumed.Message.Value,
                    Headers = new Headers
                    {
                        { "source-topic", Encoding.UTF8.GetBytes(consumed.Topic) },
                        { "source-offset", Encoding.UTF8.GetBytes(consumed.Offset.Value.ToString()) },
                        { "error", Encoding.UTF8.GetBytes(exception.Message) }
                    }
                }, stoppingToken);
                consumer.Commit(consumed);
                _failures.Remove(consumed.TopicPartitionOffset);
            }
        }
        consumer.Close();
    }

    private static IntegrationMessage ToIntegrationMessage(Message<string, string> message)
    {
        var messageId = Guid.Parse(Header(message, "message-id"));
        var type = Header(message, "message-type");
        var correlationId = Guid.Parse(Header(message, "correlation-id"));
        var causationId = Guid.Parse(Header(message, "causation-id"));
        return new IntegrationMessage(messageId, type, message.Value, message.Key,
            correlationId, causationId, DateTimeOffset.UtcNow);
    }

    private static string Header(Message<string, string> message, string key)
    {
        var bytes = message.Headers.GetLastBytes(key)
            ?? throw new InvalidOperationException($"Kafka header '{key}' is required.");
        return Encoding.UTF8.GetString(bytes);
    }
}
