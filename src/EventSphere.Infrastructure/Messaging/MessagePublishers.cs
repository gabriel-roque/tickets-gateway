using Confluent.Kafka;
using EventSphere.Application;
using Microsoft.Extensions.Logging;

namespace EventSphere.Infrastructure.Messaging;

public sealed class KafkaMessagePublisher : IMessagePublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;

    public KafkaMessagePublisher(string bootstrapServers) =>
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageSendMaxRetries = 10,
            LingerMs = 5
        }).Build();

    public async Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken)
    {
        var result = await _producer.ProduceAsync("eventsphere.events", new Message<string, string>
        {
            Key = message.PartitionKey,
            Value = message.Payload,
            Headers = new Headers
            {
                { "message-id", System.Text.Encoding.UTF8.GetBytes(message.MessageId.ToString()) },
                { "message-type", System.Text.Encoding.UTF8.GetBytes(message.MessageType) },
                { "correlation-id", System.Text.Encoding.UTF8.GetBytes(message.CorrelationId.ToString()) },
                { "causation-id", System.Text.Encoding.UTF8.GetBytes(message.CausationId.ToString()) }
            }
        }, cancellationToken);
        if (result.Status == PersistenceStatus.NotPersisted)
            throw new InvalidOperationException($"Kafka did not persist message {message.MessageId}.");
    }

    public void Dispose() => _producer.Dispose();
}

public sealed class LoggingMessagePublisher(ILogger<LoggingMessagePublisher> logger) : IMessagePublisher
{
    public Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Integration event {MessageType} {MessageId} published for {PartitionKey}",
            message.MessageType, message.MessageId, message.PartitionKey);
        return Task.CompletedTask;
    }
}
