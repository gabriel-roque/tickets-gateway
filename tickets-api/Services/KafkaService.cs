using Confluent.Kafka;
using TicketsApi.Interfaces.Services;

namespace TicketsApi.Services;

public class KafkaService(
    IConfiguration config,
    ILogger<KafkaService> logger
    ) : IKafkaService
{
    public async Task SendMessageAsync<T>(string topic, string message)
    {
        var url = config.GetValue<string>("Kafka:Url") ?? Environment.GetEnvironmentVariable("Kafka__Url");
        logger.LogInformation("KafkaService - Kafka URL: {Url}", url);
        
        var kafkaConfig = new ProducerConfig  { BootstrapServers = url };

        using var producer = new ProducerBuilder<Null, string>(kafkaConfig).Build();
        try
        {
            var deliveryResult = await producer.ProduceAsync(topic, new Message<Null, string> { Value = message });
            logger.LogInformation("Message persisted: [{Topic}: {Offset}]", deliveryResult.Topic, deliveryResult.Offset);
        }
        catch (ProduceException<Null, string> exception)
        {
            logger.LogError(exception, "Kafka failed to persist a message on {Topic}", topic);
            throw;
        }
    }
}
