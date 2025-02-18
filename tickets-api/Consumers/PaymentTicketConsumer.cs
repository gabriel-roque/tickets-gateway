using System.Text.Json;
using Confluent.Kafka;
using TicketsApi.Enums;
using TicketsApi.Interfaces.Repositories;
using TicketsApi.Models;

namespace TicketsApi.Consumers;
public class PaymentTicketConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentTicketConsumer> _logger;
    private readonly IConsumer<Ignore, string> _consumer;
    
    public PaymentTicketConsumer(
        IServiceProvider serviceProvider,
        ILogger<PaymentTicketConsumer> logger, 
        IConfiguration config
    )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;

        var kafkaUrl = config.GetValue<string>("Kafka:Url");
        
        var kafkaConfig = new ConsumerConfig
        {
            BootstrapServers = kafkaUrl,
            GroupId = $"{KafkaTopicsEnum.PaymentTicket}-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            MaxInFlight = 10,
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
                    var ticket = JsonSerializer.Deserialize<Ticket>(consumeResult.Message.Value);
                    _logger.LogInformation($"{consumerName} - [CONSUMER]: {ticket}");

                    using var scope = _serviceProvider.CreateScope();
                    ITicketRepository? ticketRepository = scope.ServiceProvider.GetService<ITicketRepository>();

                    if (ticket is not null && ticketRepository is not null)
                    {
                        await ticketRepository.Create(ticket);
                        _consumer.Commit(consumeResult);
                    }
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

