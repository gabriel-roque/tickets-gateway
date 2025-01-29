using Confluent.Kafka;
using TicketsApi.Enums;

namespace TicketsApi.Consumers;
public class PaymentTicketConsumer : BackgroundService
{
    private readonly ILogger<PaymentTicketConsumer> _logger;
    private readonly IConsumer<Ignore, string> _consumer;
    
    public PaymentTicketConsumer(
        ILogger<PaymentTicketConsumer> logger, 
        IConfiguration config
    )
    {
        _logger = logger;

        var kafkaUrl = config.GetValue<string>("Kafka:Url");
        
        var kafkaConfig = new ConsumerConfig
        {
            BootstrapServers = kafkaUrl,
            GroupId = $"{KafkaTopicsEnum.PaymentTicket}-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };
        _consumer = new ConsumerBuilder<Ignore, string>(kafkaConfig).Build();
        _consumer.Subscribe(KafkaTopicsEnum.PaymentTicket);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerName = "[PaymentTicketConsumer]";
        _logger.LogInformation($"Starting Consumer {consumerName}");
        
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer.Consume(stoppingToken);
                if (consumeResult != null)
                {
                    _logger.LogInformation($"{consumerName} - [CONSUMER]: {consumeResult.Value}");
                    _consumer.Commit(consumeResult);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"{consumerName} - [ERROR_MESSAGE]: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1), stoppingToken);
        }
    }
}

