using Confluent.Kafka;
using TicketsApi.Interfaces.Services;

namespace TicketsApi.Services;

public class KafkaService(
    IConfiguration config,
    ILogger<KafkaService> logger
    ) : IKafkaService
{
    public Task SendMessageAsync<T>(string topic, string message)
    {
        var url = config.GetValue<string>("Kafka:Url") ?? Environment.GetEnvironmentVariable("Kafka__Url");
        logger.LogInformation("KafkaService - Kafka URL: {Url}", url);
        
        var kafkaConfig = new ProducerConfig  { BootstrapServers = url };

        using (var producer = new ProducerBuilder<Null, string>(kafkaConfig).Build())
        {
            try
            {
                var deliveryResult = producer.ProduceAsync(topic, new Message<Null, string>() {Value = message}).Result;
                logger.LogInformation($"Message send: [{deliveryResult.Topic}: {deliveryResult.Value}]");
            }
            catch (ProduceException<Null, string> e)
            {
                logger.LogInformation($"Error in send message: [{e.Error.Reason}]");
            }
        }
        
        return Task.CompletedTask;
    }
}