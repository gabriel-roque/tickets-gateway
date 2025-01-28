using Confluent.Kafka;
using TicketsApi.Interfaces.Services;

namespace TicketsApi.Services;

public class KafkaService(
    IConfiguration config
    ) : IKafkaService
{
    public Task SendMessageAsync<T>(string topic, string message)
    {
        var url = config.GetValue<string>("Kafka:Url");
        var kafkaConfig = new ProducerConfig  { BootstrapServers = url };

        using (var producer = new ProducerBuilder<Null, string>(kafkaConfig).Build())
        {
            try
            {
                var deliveryResult = producer.ProduceAsync(topic, new Message<Null, string>() {Value = message}).Result;
                Console.WriteLine($"Message send: [{deliveryResult.Topic}: {deliveryResult.Value}]");
            }
            catch (ProduceException<Null, string> e)
            {
                Console.WriteLine($"Error in send message: [{e.Error.Reason}]");
            }
        }
        
        return Task.CompletedTask;
    }
}