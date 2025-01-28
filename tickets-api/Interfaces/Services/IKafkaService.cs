namespace TicketsApi.Interfaces.Services;

public interface IKafkaService
{
    Task SendMessageAsync<T>(string topic, string message);
}